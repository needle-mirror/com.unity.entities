#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Entities;
using Unity.Entities.Content;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [DisallowMultipleComponent]
    public class WeakMaterialRefComponentAuthoring : MonoBehaviour
    {
        public WeakObjectReference<Material> matRef;
    }

    public struct WeakMaterialRefComponent : ISharedComponentData
    {
        public WeakObjectReference<Material> materialRef;
    }

    public class WeakMaterialRefComponentAuthoringBaker : Baker<WeakMaterialRefComponentAuthoring>
    {
        public override void Bake(WeakMaterialRefComponentAuthoring authoring)
        {
            AddSharedComponentManaged(GetEntity(authoring), new WeakMaterialRefComponent() { materialRef = authoring.matRef });
        }
    }
}
#endif
