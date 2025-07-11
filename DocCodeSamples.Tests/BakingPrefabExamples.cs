using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;

// The files in this namespace are used to test the code samples in the documentation.
namespace Doc.CodeSamples.Tests
{

    #region EntityPrefabInSubScene
    public struct EntityPrefabComponent : IComponentData
    {
        public Entity Value;
    }

    public class EntityPrefabAuthoring : MonoBehaviour
    {
        public GameObject Prefab;
    }

    public class EntityPrefabBaker : Baker<EntityPrefabAuthoring>
    {
        public override void Bake(EntityPrefabAuthoring authoring)
        {
            // Register the Prefab in the Baker
            var entityPrefab = GetEntity(authoring.Prefab, TransformUsageFlags.Dynamic);
            // Add the Entity reference to a component for instantiation later
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EntityPrefabComponent() {Value = entityPrefab});
        }
    }
    #endregion

    #region EntityPrefabReferenceInSubScene
    public struct EntityPrefabReferenceComponent : IComponentData
    {
        public EntityPrefabReference Value;
    }

    public class EntityPrefabReferenceAuthoring : MonoBehaviour
    {
        public GameObject Prefab;
    }

    public class EntityPrefabReferenceBaker : Baker<EntityPrefabReferenceAuthoring>
    {
        public override void Bake(EntityPrefabReferenceAuthoring authoring)
        {
            // Create an EntityPrefabReference from a GameObject. This will allow the
            // serialization process to serialize the prefab in its own entity scene
            // file instead of duplicating the prefab ECS content everywhere it is used
            var entityPrefabReference = new EntityPrefabReference(authoring.Prefab);
            var entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new EntityPrefabReferenceComponent() {Value = entityPrefabReference});
        }
    }
    #endregion

    #region InstantiateEmbeddedPrefabs
    public partial struct InstantiatePrefabSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Get all Entities that have the component with the Entity reference
            foreach (var prefab in
                     SystemAPI.Query<RefRO<EntityPrefabComponent>>())
            {
                // Instantiate the prefab Entity
                var instance = ecb.Instantiate(prefab.ValueRO.Value);
                // Note: the returned instance is only relevant when used in the ECB
                // as the entity is not created in the EntityManager until ECB.Playback
                ecb.AddComponent<ComponentA>(instance);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
    #endregion


    #region InstantiateLoadedPrefabs
    public partial struct InstantiatePrefabReferenceSystem : ISystem
    {
        public void OnStartRunning(ref SystemState state)
        {
            // Add the RequestEntityPrefabLoaded component to entities that have an
            // EntityPrefabReference component and load a prefab to them.
            // The PrefabLoadResult component is added to an entity once a prefab is loaded.
            // Note: it might take a few frames for the prefab to load.
            foreach (var (prefab, entity) in
                     SystemAPI.Query<RefRO<EntityPrefabReferenceComponent>>().WithNone<PrefabLoadResult>().WithEntityAccess())
            {
                state.EntityManager.AddComponentData(entity, new RequestEntityPrefabLoaded(){ Prefab = prefab.ValueRO.Value} );
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // The PrefabLoadResult component indicates that Unity loaded a prefab
            // and added it to the entity.
            // You can access the prefab from the PrefabLoadResult component and instantiate it.
            foreach (var (prefab, entity) in
                     SystemAPI.Query<RefRO<PrefabLoadResult>>().WithEntityAccess())
            {
                var instance = ecb.Instantiate(prefab.ValueRO.PrefabRoot);

                // Remove both RequestEntityPrefabLoaded and PrefabLoadResult to prevent
                // the prefab being loaded and instantiated multiple times, respectively
                ecb.RemoveComponent<RequestEntityPrefabLoaded>(entity);
                ecb.RemoveComponent<PrefabLoadResult>(entity);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
    #endregion


    public partial struct PrefabsInQueriesSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            #region PrefabsInQueries
            // This query will return all baked entities, including the prefab entities
            var prefabQuery = SystemAPI.QueryBuilder()
                .WithAll<BakedEntity>().WithOptions(EntityQueryOptions.IncludePrefab).Build();
            #endregion

            #region DestroyPrefabs
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (component, entity) in
                     SystemAPI.Query<RefRO<RotationSpeed>>().WithEntityAccess())
            {
                if (component.ValueRO.RadiansPerSecond <= 0)
                {
                    ecb.DestroyEntity(entity);
                }
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            #endregion
        }
    }
}
