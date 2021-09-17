using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Entities.Conversion;
using UnityEngine;

namespace Unity.Entities.Editor
{
    static class EntityConversionUtility
    {
        static EntityConversionUtility()
        {
            TypeManager.Initialize();
        }

        public static void GetConversionData(List<GameObject> gameObjects, World world, List<EntityConversionData> result)
        {
            result.Clear();
            if (world == null)
                return;

            using var cachedEntities = new NativeList<Entity>(Allocator.Temp);
            var lookup = world.EntityManager.Debug.GetCachedEntityGUIDToEntityIndexLookup();
            var mappingSystem = world.GetExistingSystem<GameObjectConversionMappingSystem>();

            foreach (var gameObject in gameObjects)
            {
                if (!IsGameObjectConverted(gameObject))
                    continue;

                cachedEntities.Clear();
                foreach (var e in lookup.GetValuesForKey(gameObject.GetInstanceID()))
                    cachedEntities.Add(e);

                if (cachedEntities.Length == 0)
                    continue;

                result.Add(new EntityConversionData
                {
                    PrimaryEntity = cachedEntities[0],
                    AdditionalEntities = cachedEntities.ToArrayNBC(),
                    EntityManager = world.EntityManager,
                    LogEvents = mappingSystem?.JournalData.SelectLogEventsOrdered(gameObject)
                });
            }
        }

        public static EntityConversionData GetConversionData(GameObject gameObject, World world)
        {
            if (world == null || !IsGameObjectConverted(gameObject))
                return EntityConversionData.Null;

            var mappingSystem = world.GetExistingSystem<GameObjectConversionMappingSystem>();
            using var cachedEntities = new NativeList<Entity>(Allocator.Temp);
            world.EntityManager.Debug.GetEntitiesForAuthoringObject(gameObject, cachedEntities);

            if (cachedEntities.Length == 0)
                return EntityConversionData.Null;

            return new EntityConversionData
            {
                PrimaryEntity = cachedEntities[0],
                AdditionalEntities = cachedEntities.ToArrayNBC(),
                EntityManager = world.EntityManager,
                LogEvents = mappingSystem?.JournalData.SelectLogEventsOrdered(gameObject)
            };
        }

        public static bool IsGameObjectConverted(GameObject gameObject)
        {
            return GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(gameObject).IsConverted();
        }
    }
}
