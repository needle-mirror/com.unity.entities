#if !UNITY_DISABLE_MANAGED_COMPONENTS

/*
 * Hybrid Components are classic Unity components that are added to an entity via AddComponentObject.
 * We call Companion GameObject the GameObject owned by an entity in order to host those Hybrid Components.
 * Companion GameObjects should be considered implementation details and are not intended to be directly accessed by users.
 * An entity can also have Hybrid Components owned externally (this is used during conversion), but this is not what the system below is about.
 * When an entity owns a Companion GameObject, the entity also has a managed CompanionLink, which contains a reference to that GameObject.
 * Companion GameObjects are in world space, their transform is updated from their entities, never the other way around.
 * Getting to the Companion GameObject from an Entity is done through the managed CompanionLink.
 * Going the other way around, from the Companion GameObject to the Entity, isn't possible nor advised.
 */

using Unity.Collections;
using UnityEngine;

namespace Unity.Entities
{

    // Needs to be a cleanup component because instantiation will always create disabled GameObjects
    struct CompanionGameObjectActiveCleanup : ICleanupComponentData
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    partial class CompanionGameObjectUpdateSystem : SystemBase
    {
        private EntityQuery toActivate;
        private EntityQuery toDeactivate;
        private EntityQuery toCleanup;

        protected override void OnCreate()
        {
            base.OnCreate();
            toActivate = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CompanionLink>()
                .WithNone<CompanionGameObjectActiveCleanup, Disabled>()
                .Build(this);
            toDeactivate = new EntityQueryBuilder(Allocator.Temp)
                .WithAny<Disabled, Prefab>()
                .WithAll<CompanionGameObjectActiveCleanup, CompanionLink>()
                .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)
                .Build(this);
            toCleanup = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<CompanionGameObjectActiveCleanup>()
                .WithNone<CompanionLink>()
                .Build(this);
        }

        protected override void OnUpdate()
        {
            Entities
                .WithNone<CompanionGameObjectActiveCleanup, Disabled>()
                .WithAll<CompanionLink>()
                .ForEach((CompanionLink link) => link.Companion.SetActive(true)).WithoutBurst().Run();
            EntityManager.AddComponent<CompanionGameObjectActiveCleanup>(toActivate);

            Entities
                .WithAny<Disabled, Prefab>()
                .WithAll<CompanionGameObjectActiveCleanup, CompanionLink>()
                .WithEntityQueryOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab)
                .ForEach((CompanionLink link) => link.Companion.SetActive(false)).WithoutBurst().Run();
            EntityManager.RemoveComponent<CompanionGameObjectActiveCleanup>(toDeactivate);

            EntityManager.RemoveComponent<CompanionGameObjectActiveCleanup>(toCleanup);
        }
    }
}
#endif
