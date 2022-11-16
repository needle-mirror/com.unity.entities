using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Entities.Editor
{
    static class EntityBakingEditorUtility
    {
        static EntityBakingEditorUtility()
        {
            TypeManager.Initialize();
        }

        public static void GetBakingData(List<GameObject> gameObjects, World world, List<EntityBakingData> result)
        {
            result.Clear();
            if (world == null)
                return;

            using var cachedEntities = new NativeList<Entity>(Allocator.Temp);
            var lookup = world.EntityManager.Debug.GetCachedEntityGUIDToEntityIndexLookup();

            foreach (var gameObject in gameObjects)
            {
                if (!IsGameObjectBaked(gameObject))
                    continue;

                cachedEntities.Clear();
                foreach (var e in lookup.GetValuesForKey(gameObject.GetInstanceID()))
                    cachedEntities.Add(e);

                if (cachedEntities.Length == 0)
                    continue;

                result.Add(new EntityBakingData
                {
                    PrimaryEntity = cachedEntities[0],
                    AdditionalEntities = cachedEntities.ToArrayNBC(),
                    EntityManager = world.EntityManager,
                });
            }
        }

        public static EntityBakingData GetBakingData(GameObject gameObject, World world)
        {
            if (world == null || !IsGameObjectBaked(gameObject))
                return EntityBakingData.Null;

            using var cachedEntities = new NativeList<Entity>(Allocator.Temp);
            world.EntityManager.Debug.GetEntitiesForAuthoringObject(gameObject, cachedEntities);

            if (cachedEntities.Length == 0)
                return EntityBakingData.Null;

            return new EntityBakingData
            {
                PrimaryEntity = cachedEntities[0],
                AdditionalEntities = cachedEntities.ToArrayNBC(),
                EntityManager = world.EntityManager,
            };
        }

        public static bool IsGameObjectBaked(GameObject gameObject)
            => GameObjectBakingEditorUtility.GetGameObjectBakingResultStatus(gameObject).IsBaked();
    }
}
