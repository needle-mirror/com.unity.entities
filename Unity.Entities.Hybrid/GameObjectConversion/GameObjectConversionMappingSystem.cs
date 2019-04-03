using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;
using Hash128 = Unity.Entities.Hash128;

[DisableAutoCreation]
class GameObjectConversionMappingSystem : ComponentSystem
{
    Dictionary<GameObject, List<Entity>>  m_GameObjectToEntity = new Dictionary<GameObject, List<Entity>>();
    
    HashSet<GameObject>                   m_ReferencedPrefabs = new HashSet<GameObject>();
    HashSet<GameObject>                   m_LinkedEntityGroup = new HashSet<GameObject>();
    
    World                                 m_DstWorld;
    EntityManager                         m_DstManager;
    bool                                  m_AddEntityGUID;
    Hash128                m_SceneGUID;
    
    public World DstWorld { get { return m_DstWorld; } }
    public bool AddEntityGUID { get { return m_AddEntityGUID; } }

    public GameObjectConversionMappingSystem(World DstWorld, Hash128 sceneGUID, bool addEntityGuid)
    {
        m_DstWorld = DstWorld;
        m_SceneGUID = sceneGUID;
        m_AddEntityGUID = addEntityGuid;
        m_DstManager = DstWorld.GetOrCreateManager<EntityManager>();
    }

    List<Entity> GetList(GameObject go)
    {
        List<Entity> list;
        if (!m_GameObjectToEntity.TryGetValue(go, out list))
            m_GameObjectToEntity[go] = list = new List<Entity>();

        return list;
    }
   
    unsafe public static EntityGuid GetEntityGuid(GameObject go, int index)
    {
#if false
        var id = GlobalObjectId.GetGlobalObjectId(go);
        // For the time being use InstanceID until we support GlobalObjectID API
        //Debug.Log(id);
        var hash = Hash128.Compute($"{id}:{index}");
        
        EntityGuid entityGuid;
        Assert.AreEqual(sizeof(EntityGuid), sizeof(Hash128));
        UnsafeUtility.MemCpy(&entityGuid, &hash, sizeof(Hash128));
        return entityGuid;
#else
        EntityGuid entityGuid;
        entityGuid.a = (ulong)go.GetInstanceID();
        entityGuid.b = (ulong)index;
    
        return entityGuid;
#endif
    }

    Entity CreateEntity(GameObject go, int index)
    {
        Entity entity;

        if (m_AddEntityGUID)
        {
            entity = m_DstManager.CreateEntity(ComponentType.ReadWrite<EntityGuid>());
            var entityGuid = GetEntityGuid(go, index);
            m_DstManager.SetComponentData(entity, entityGuid);
        }
        else
        {
            entity = m_DstManager.CreateEntity();
        }

        var section = go.GetComponentInParent<SceneSectionComponent>();
        if (m_SceneGUID != default(Hash128))
            m_DstManager.AddSharedComponentData(entity, new SceneSection { SceneGUID = m_SceneGUID, Section = section != null ? section.SectionIndex : 0});
        

        if (go.GetComponentInParent<StaticOptimizeEntity>() != null)
            m_DstManager.AddComponentData(entity, new Static());

#if UNITY_EDITOR
        m_DstManager.SetName(entity, go.name);
#endif


        return entity;
    }

    public Entity GetPrimaryEntity(GameObject go)
    {
        if (go == null)
            return new Entity();
        
        var list = GetList(go);
        if (list.Count == 0)
        {
            var entity = CreateEntity(go, list.Count);
            #if UNITY_EDITOR
            m_DstManager.SetName(entity, go.name);
            #endif

            list.Add(entity);
            return entity;
        }
        else
        {
            return list[0];
        }
    }
    
    public Entity CreateAdditionalEntity(GameObject go)
    {
        if (go == null)
            throw new System.ArgumentException("CreateAdditionalEntity must be called with a valid game object");

        var list = GetList(go);
        if (list.Count == 0)
            throw new System.ArgumentException("CreateAdditionalEntity can't be called before GetPrimaryEntity is called for that game object");

        var entity = CreateEntity(go, list.Count);
        list.Add(entity);
        return entity;
    }
    
    public IEnumerable<Entity> GetEntities(GameObject go)
    {
        return GetList(go);
    }

    public void AddGameObjectOrPrefabAsGroup(GameObject prefab)
    {
        if (prefab == null)
            return;
        if (m_ReferencedPrefabs.Contains(prefab))
            return;

        m_LinkedEntityGroup.Add(prefab);
        
        var isPrefab = !prefab.scene.IsValid();
        if (isPrefab)
            CreateEntitiesForGameObjectsRecurse(prefab.transform, EntityManager, m_ReferencedPrefabs);
        else
            CreateEntitiesForGameObjectsRecurse(prefab.transform, EntityManager, null);
    }

    public void AddReferencedPrefabAsGroup(GameObject prefab)
    {
        if (prefab == null)
            return;
        if (m_ReferencedPrefabs.Contains(prefab))
            return;

        var isPrefab = !prefab.scene.IsValid();

        if (!isPrefab)
            return;

        m_LinkedEntityGroup.Add(prefab);
        CreateEntitiesForGameObjectsRecurse(prefab.transform, EntityManager, m_ReferencedPrefabs);
    }
    
    internal void AddPrefabComponentDataTag()
    {
        // Add prefab tag to all entities that were converted from a prefab game object source
        foreach (var i in m_GameObjectToEntity)
        {
            if (m_ReferencedPrefabs.Contains(i.Key))
            {
                foreach (var e in i.Value)
                {
                    m_DstManager.AddComponentData(e, new Prefab());
                }
            }
        }

        // Create LinkedEntityGroup for each root prefab entity
        // Instnatiate & Destroy will destroy the entity as a group.
        foreach (var i in m_LinkedEntityGroup)
        {
            var allChildren = i.GetComponentsInChildren<Transform>(true);

            var linkedRoot = GetPrimaryEntity(i);
            var buffer = m_DstManager.AddBuffer<LinkedEntityGroup>(linkedRoot);

            foreach (Transform t in allChildren)
            {
                List<Entity> entities;
                if (!m_GameObjectToEntity.TryGetValue(t.gameObject, out entities))
                    continue;

                foreach (var e in entities)
                    buffer.Add(e);                    
            }
        }
    }
    
    internal static void CreateEntitiesForGameObjectsRecurse(Transform transform, EntityManager gameObjectEntityManager, HashSet<GameObject> gameObjects)
    {
        GameObjectEntity.AddToEntityManager(gameObjectEntityManager, transform.gameObject);
        if (gameObjects != null)
            gameObjects.Add(transform.gameObject);
        
        foreach (Transform child in transform)
            CreateEntitiesForGameObjectsRecurse(child, gameObjectEntityManager, gameObjects);
    }
    

    internal static void CreateEntitiesForGameObjects(Scene scene, World gameObjectWorld)
    {
        var entityManager = gameObjectWorld.GetOrCreateManager<EntityManager>();
        var gameObjects = scene.GetRootGameObjects();

        foreach (var go in gameObjects)
            CreateEntitiesForGameObjectsRecurse(go.transform, entityManager, null);
    }
    
    protected override void OnUpdate()
    {
        
    }
}
