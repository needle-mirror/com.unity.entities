using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

[GameObjectToEntityConversion]
public abstract class GameObjectConversionSystem : ComponentSystem
{
    public World DstWorld;
    public EntityManager DstEntityManager;

    GameObjectConversionMappingSystem m_MappingSystem;

    protected override void OnCreateManager()
    {
        base.OnCreateManager();

        m_MappingSystem = World.GetOrCreateManager<GameObjectConversionMappingSystem>();
        DstWorld = m_MappingSystem.DstWorld;
        DstEntityManager = DstWorld.GetOrCreateManager<EntityManager>();
    }
    
    public Entity GetPrimaryEntity(Component component)
    {
        return m_MappingSystem.GetPrimaryEntity(component != null ? component.gameObject : null);
    }
    
    public Entity CreateAdditionalEntity(Component component)
    {
        return m_MappingSystem.CreateAdditionalEntity(component != null ? component.gameObject : null);
    }
    
    public IEnumerable<Entity> GetEntities(Component component)
    {
        return m_MappingSystem.GetEntities(component != null ? component.gameObject : null);
    }
    
    public Entity GetPrimaryEntity(GameObject gameObject)
    {
        return m_MappingSystem.GetPrimaryEntity(gameObject);
    }
    
    public Entity CreateAdditionalEntity(GameObject gameObject)
    {
        return m_MappingSystem.CreateAdditionalEntity(gameObject);
    }
    
    public IEnumerable<Entity> GetEntities(GameObject gameObject)
    {
        return m_MappingSystem.GetEntities(gameObject);
    }
    
    public void AddReferencedPrefab(GameObject gameObject)
    {
        m_MappingSystem.AddReferencedPrefab(gameObject);
    }
}