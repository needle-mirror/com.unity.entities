using System;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities
{
    public static class EntityManagerExtensions
    {
        [Obsolete("This function is equivalent to creating an empty entity since there are no proxy components any more. (RemovedAfter 2020-11-30)")]
        public static Entity Instantiate(this EntityManager entityManager, GameObject srcGameObject)
        {
            return entityManager.CreateEntity();
        }

        [Obsolete("This function is equivalent to creating an empty entities since there are no proxy components any more. (RemovedAfter 2020-11-30)")]
        public static void Instantiate(this EntityManager entityManager, GameObject srcGameObject, NativeArray<Entity> outputEntities)
        {
            if (outputEntities.Length == 0)
                return;
            entityManager.CreateEntity(entityManager.CreateArchetype(), outputEntities);
        }

        public static unsafe T GetComponentObject<T>(this EntityManager entityManager, Entity entity) where T : Component
        {
            var access = entityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            var typeIndex = TypeManager.GetTypeIndex<T>();
            var index = *access->GetManagedComponentIndex(entity, typeIndex);
            return (T)mcs.GetManagedComponent(index);
        }
    }
}
