namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    using UnityEngine;
    using Unity.Entities;
    using Unity.Mathematics;

    class SpawnerAuthoring : MonoBehaviour
    {
        public GameObject Prefab;
        public float SpawnRate;
    }

    class SpawnerBaker : Baker<SpawnerAuthoring>
    {
        public override void Bake(SpawnerAuthoring authoring)
        {
            // This line converts the Spawner GameObject into an Entity.
            // TransformUsageFlags is None because the Spawner entity is not
            // rendered and does not need a LocalTransform component.
            var entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new Spawner
            {
                // This GetEntity call converts a GameObject prefab into an entity
                // prefab. The prefab is rendered, so it requires the standard Transform
                // components, that's why TransformUsageFlags is set to Dynamic.
                Prefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic),
                SpawnPosition = authoring.transform.position,
                SpawnRate = authoring.SpawnRate,
                NextSpawnTime = 0f
            });
        }
    }

    #region spawner-component
    public struct Spawner : IComponentData
    {
        public Entity Prefab;
        public float3 SpawnPosition;
        public float SpawnRate;
        // This field is used only for the multi-threading example.
        public float NextSpawnTime;
    }
    #endregion
    #endregion    
}