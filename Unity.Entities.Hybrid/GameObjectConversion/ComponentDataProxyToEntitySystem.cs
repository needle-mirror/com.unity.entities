using Unity.Entities;
using UnityEngine;

class ComponentDataProxyToEntitySystem : GameObjectConversionSystem
{
    protected override void OnUpdate()
    {
        ForEach((Transform transform) => 
        {            
            GameObjectEntity.CopyAllComponentsToEntity(transform.gameObject, DstEntityManager, GetPrimaryEntity(transform));
        });
    }
}