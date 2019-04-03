using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

#pragma warning disable 162


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

    public static void ConvertScene(Scene scene, World dstEntityWorld, bool addEntityGUID = false)
    {    
        m_ConvertScene.Begin();
        m_CreateConversionWorld.Begin();
        using (var gameObjectWorld = new World("GameObject World"))
        {            
            var mappingSystem = gameObjectWorld.CreateManager<GameObjectConversionMappingSystem>(dstEntityWorld);
            mappingSystem.AddEntityGUID = addEntityGUID;
            AddConversionSystems(gameObjectWorld);

            m_CreateConversionWorld.End();

            // Create Entities from game objects
            m_CreateEntitiesForGameObjects.Begin();
            GameObjectConversionMappingSystem.CreateEntitiesForGameObjects(scene, gameObjectWorld);
            m_CreateEntitiesForGameObjects.End();

            m_UpdateSystems.Begin();
            // Convert all the data into dstEntityWorld
            var managers = gameObjectWorld.BehaviourManagers;
            foreach (var manager in managers)
                manager.Update();
            m_UpdateSystems.End();
    
            m_AddPrefabComponentDataTag.Begin();
            mappingSystem.AddPrefabComponentDataTag();
            m_AddPrefabComponentDataTag.End();
            
            m_DestroyConversionWorld.Begin();
        }
        m_DestroyConversionWorld.End();
        m_ConvertScene.End();
    }
    
    static void AddConversionSystems(World gameObjectWorld)
    {
        // Ensure the following systems run first in this order...
        gameObjectWorld.GetOrCreateManager<ConvertGameObjectToEntitySystemDeclarePrefabs>();
        gameObjectWorld.GetOrCreateManager<ConvertGameObjectToEntitySystem>();
        gameObjectWorld.GetOrCreateManager<ComponentDataProxyToEntitySystem>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!TypeManager.IsAssemblyReferencingEntities(assembly))
                continue;
            
            try
            {
                var allTypes = assembly.GetTypes();
                CreateBehaviourManagersForMatchingTypes(allTypes, gameObjectWorld);
            }
            catch (ReflectionTypeLoadException)
            {
                Debug.LogWarning($"DefaultWorldInitialization failed loading assembly: {(assembly.IsDynamic ? assembly.ToString() : assembly.Location)}");
                continue;
            }
        }
    }

    static void CreateBehaviourManagersForMatchingTypes(IEnumerable<Type> allTypes, World world)
    {
        var systemTypes = allTypes.Where(t =>
            t.IsSubclassOf(typeof(ComponentSystemBase)) &&
            !t.IsAbstract &&
            !t.ContainsGenericParameters &&
            t.GetCustomAttributes(typeof(DisableAutoCreationAttribute), true).Length == 0 &&
            t.GetCustomAttributes(typeof(GameObjectToEntityConversionAttribute), true).Length != 0);

        foreach (var type in systemTypes)
        {
            GetBehaviourManagerAndLogException(world, type);
        }
    }
    
    static void GetBehaviourManagerAndLogException(World world, Type type)
    {
        try
        {
            world.GetOrCreateManager(type);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }
}