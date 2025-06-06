using Unity.Entities;
using Unity.Transforms;
using System;
using Unity.Mathematics;
using UnityEngine;

namespace Doc.CodeSamples.Tests
{
    #region add-component-single-entity
    public partial struct AddComponentToSingleEntitySystemExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<Rotation>(entity);
        }

    }
    #endregion

    #region add-component-multiple-entities
    struct ComponentA : IComponentData {}
    struct ComponentB : IComponentData {}
    public partial struct AddComponentToMultipleEntitiesSystemExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var query = state.GetEntityQuery(typeof(ComponentA));
            state.EntityManager.AddComponent<ComponentB>(query);
        }
    }
    #endregion

    #region remove-component
    public partial struct RemoveComponentSystemExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var query = state.GetEntityQuery(typeof(Rotation));
            state.EntityManager.RemoveComponent<Rotation>(query);
        }
    }
    #endregion

    #region set-component-single-entity
    public partial struct SetComponentOnSingleEntitySystemExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<Rotation>(entity);
            state.EntityManager.SetComponentData<Rotation>(entity, new Rotation { Value = quaternion.identity });
        }
    }
    #endregion

    #region get-component-single-entity
     public partial struct GetComponentOnSingleEntitySystemExample : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            var entity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponent<Rotation>(entity);

            // Get the Rotation component
            var rotationComponent = state.EntityManager.GetComponentData<Rotation>(entity);
        }
    }
    #endregion

    #region set-component-system-entity
    public partial struct SetComponentOnSystemEntitySystemExample : ISystem
    {
        public struct MyComponent : IComponentData
        {
            public int Value;
        }
        public void OnCreate(ref SystemState state)
        {
            // Add MyComponent to this system's entity.
            state.EntityManager.AddComponentData(state.SystemHandle, new MyComponent());
        }
        public void OnUpdate(ref SystemState state)
        {
            // Modify the value of MyComponent on this system's entity.
            state.EntityManager.SetComponentData(state.SystemHandle, new MyComponent{Value = 6});
        }
    }
    #endregion


#if !UNITY_DISABLE_MANAGED_COMPONENTS
    #region managed-component-external-resource
    public class ManagedComponentWithExternalResource : IComponentData, IDisposable, ICloneable
    {
        public ParticleSystem ParticleSystem;

        public void Dispose()
        {
            UnityEngine.Object.Destroy(ParticleSystem);
        }

        public object Clone()
        {
            return new ManagedComponentWithExternalResource { ParticleSystem = UnityEngine.Object.Instantiate(ParticleSystem) };
        }
    }
    #endregion
#endif
}
