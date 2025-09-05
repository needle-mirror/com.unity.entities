namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    #region HelloComponent
    using Unity.Entities;
    using Unity.Collections;
    using UnityEngine;

    // This is an example of an unmanaged ECS component.
    public struct HelloComponent : IComponentData
    {
        // FixedString32Bytes is used instead of string, because
        // struct IComponentData can only contain unmanaged types.
        public FixedString32Bytes Message;
    }
    #endregion

    #region ExampleSystem
    public partial struct ExampleSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            // Initialize and add a HelloComponent component to the entity.
            state.EntityManager.AddComponentData(entity, new HelloComponent 
                { Message = "Hello ECS World" });
            // Set the name of the entity to make it easier to identify it.
            // Note: the entity Name property only exists in the Editor.
            state.EntityManager.SetName(entity, "Hello World Entity");
        }

        public void OnUpdate(ref SystemState state)
        {
            // The query retrieves all entities with a HelloComponent component.
            foreach (var message in
                        SystemAPI.Query<RefRO<HelloComponent>>())
            {
                Debug.Log(message.ValueRO.Message);
            }
        }
    }
    #endregion
    #endregion
}