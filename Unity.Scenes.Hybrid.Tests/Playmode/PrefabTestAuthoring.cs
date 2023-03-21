using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;

namespace Unity.Scenes.Hybrid.Tests.Playmode
{
    public struct PrefabTestComponent : IComponentData
    {
        public EntityPrefabReference PrefabReference;
    }

#if UNITY_EDITOR
    public class PrefabTestAuthoring : MonoBehaviour
    {
        public GameObject _Prefab;

        class Baker : Baker<PrefabTestAuthoring>
        {
            public override void Bake(PrefabTestAuthoring authoring)
            {
                // This test shouldn't require transform components
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new PrefabTestComponent(){PrefabReference = new EntityPrefabReference(authoring._Prefab)});
            }
        }
    }
#endif
}
