using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;

namespace Doc.CodeSamples.Tests
{
    struct SomeComponent:IComponentData
    {

    }
    #region linked-entity-group
    partial struct SomeSystem:ISystem

    {
        public void OnUpdate(ref SystemState state)
        {
            //Get the targeted entity for which you would like to modify the LinkedEntityGroup
            var q = SystemAPI.QueryBuilder().WithAll<SomeComponent>().WithAll<LinkedEntityGroup>().Build().ToEntityArray(Allocator.Temp);

            //Create the child entity and add the SceneTag
            var child = state.EntityManager.CreateEntity();
            var sceneTag = state.EntityManager.GetSharedComponent<SceneTag>(q[0]);
            state.EntityManager.AddSharedComponent<SceneTag>(child, sceneTag);

            //If needed add the new entity as a child in the transform hierarchy 
            state.EntityManager.AddComponentData(child, new Parent { Value = q[0] });

            //Get the LinkedEntityGroup and add the newly created child entity
            var leg = SystemAPI.GetBuffer<LinkedEntityGroup>(q[0]);
            leg.Add(child);

            state.Enabled = false;
        }
    }

    #endregion
}