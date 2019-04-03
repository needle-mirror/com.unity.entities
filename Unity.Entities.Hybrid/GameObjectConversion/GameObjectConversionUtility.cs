﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

#pragma warning disable 162

namespace Unity.Entities
{
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    class GameObjectConversionInitializationGroup : ComponentSystemGroup
    {
        
    }
    
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    class GameObjectConversionGroup : ComponentSystemGroup
    {
        
    }
    
    public static class GameObjectConversionUtility
    {
        static ProfilerMarker m_ConvertScene = new ProfilerMarker("GameObjectConversionUtility.ConvertScene");
        static ProfilerMarker m_CreateConversionWorld = new ProfilerMarker("Create World & Systems");
        static ProfilerMarker m_DestroyConversionWorld = new ProfilerMarker("DestroyWorld");
        static ProfilerMarker m_CreateEntitiesForGameObjects = new ProfilerMarker("CreateEntitiesForGameObjects");
        static ProfilerMarker m_UpdateSystems = new ProfilerMarker("UpdateConversionSystems");
        static ProfilerMarker m_AddPrefabComponentDataTag = new ProfilerMarker("AddPrefabComponentDataTag");
    
        unsafe public static EntityGuid GetEntityGuid(GameObject gameObject, int index)
        {
            return GameObjectConversionMappingSystem.GetEntityGuid(gameObject, index);
        }
    
        internal  static World CreateConversionWorld(World dstEntityWorld, Hash128 sceneGUID, bool addEntity)
        {
            m_CreateConversionWorld.Begin();

            var gameObjectWorld = new World("GameObject World");
            gameObjectWorld.CreateManager<GameObjectConversionMappingSystem>(dstEntityWorld, sceneGUID, addEntity);

            AddConversionSystems(gameObjectWorld);

            m_CreateConversionWorld.End();

            return gameObjectWorld;
        }
        
        
        internal static void Convert(World gameObjectWorld, World dstEntityWorld)
        {
            var mappingSystem = gameObjectWorld.GetExistingManager<GameObjectConversionMappingSystem>();

            using (m_UpdateSystems.Auto())
            {
                // Convert all the data into dstEntityWorld
                gameObjectWorld.GetExistingManager<GameObjectConversionInitializationGroup>().Update();
                gameObjectWorld.GetExistingManager<GameObjectConversionGroup>().Update();
            }

            using (m_AddPrefabComponentDataTag.Auto())
            {
                mappingSystem.AddPrefabComponentDataTag();    
            }
        }

        internal static Entity GameObjectToConvertedEntity(World gameObjectWorld, GameObject gameObject)
        {
            var mappingSystem = gameObjectWorld.GetExistingManager<GameObjectConversionMappingSystem>();
            return mappingSystem.GetPrimaryEntity(gameObject);
        }


        public static Entity ConvertGameObjectHierarchy(GameObject root, World dstEntityWorld)
        {
            m_ConvertScene.Begin();
            
            Entity convertedEntity;
            using (var gameObjectWorld = CreateConversionWorld(dstEntityWorld, default(Hash128), false))
            {
                var mappingSystem = gameObjectWorld.GetExistingManager<GameObjectConversionMappingSystem>();

                using (m_CreateEntitiesForGameObjects.Auto())
                {
                    mappingSystem.AddGameObjectOrPrefabAsGroup(root);
                }

                Convert(gameObjectWorld, dstEntityWorld);

                convertedEntity = mappingSystem.GetPrimaryEntity(root);
                m_DestroyConversionWorld.Begin();
            }
            m_DestroyConversionWorld.End();
            
            m_ConvertScene.End();

            return convertedEntity;
        }
    
        public static void ConvertScene(Scene scene, Hash128 sceneHash, World dstEntityWorld, bool addEntityGUID = false)
        {    
            m_ConvertScene.Begin();
            using (var gameObjectWorld = CreateConversionWorld(dstEntityWorld, sceneHash, addEntityGUID))
            {                
                using (m_CreateEntitiesForGameObjects.Auto())
                {
                    GameObjectConversionMappingSystem.CreateEntitiesForGameObjects(scene, gameObjectWorld);
                }
                
                Convert(gameObjectWorld, dstEntityWorld);
                
                m_DestroyConversionWorld.Begin();
            }
            m_DestroyConversionWorld.End();
            
            m_ConvertScene.End();
        }
        
        static void AddConversionSystems(World gameObjectWorld)
        {
            var init = gameObjectWorld.GetOrCreateManager<GameObjectConversionInitializationGroup>();
            
            // Ensure the following systems run first in this order...
            init.AddSystemToUpdateList(gameObjectWorld.GetOrCreateManager<ConvertGameObjectToEntitySystemDeclarePrefabs>());
            init.AddSystemToUpdateList(gameObjectWorld.GetOrCreateManager<ComponentDataProxyToEntitySystem>());

            var convert = gameObjectWorld.GetOrCreateManager<GameObjectConversionGroup>();
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.GameObjectConversion);
            foreach (var system in systems)
                AddSystemAndLogException(gameObjectWorld, convert, system);
                        
            convert.SortSystemUpdateList();
        }
    
        static void AddSystemAndLogException(World world, ComponentSystemGroup group, Type type)
        {
            try
            {
                group.AddSystemToUpdateList(world.GetOrCreateManager(type) as ComponentSystemBase);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }    
}
