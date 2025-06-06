using System;
using Unity.Entities;

namespace Doc.CodeSamples.Tests
{
    #region buffer
    [InternalBufferCapacity(16)]
    public struct ExampleBufferComponent : IBufferElementData
    {
        public int Value;
    }
    #endregion

    #region chunk
    public struct ExampleChunkComponent : IComponentData
    {
        public int Value;
    }
    #endregion

    #region system-state-shared
    public struct ExampleSharedCleanupComponent : ICleanupSharedComponentData
    {
        public int Value;
    }
    #endregion

    #region system-state
    public struct ExampleCleanupComponent : ICleanupComponentData
    {

    }
    #endregion

    #region add-cleanup
    public partial struct AddCleanupSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            foreach (var (tag, entity) in SystemAPI.Query<ExampleTagComponent>().WithEntityAccess())
            {
                // Add the cleanup component to all entities with the tag component.
                ecb.AddComponent<ExampleCleanupComponent>(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            state.Enabled = false;
        }
    }
    #endregion

    #region destroy-entity
    [UpdateAfter(typeof(AddCleanupSystem))]
    public partial struct DestructionSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            foreach (var (tag, entity) in SystemAPI.Query<ExampleTagComponent>().WithAll<ExampleCleanupComponent>().WithEntityAccess())
            {
                // Destroy the Entity, which means all components, except for the cleanup component, gets removed.
                ecb.DestroyEntity(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            state.Enabled = false;
        }
    }
    #endregion

    #region remove-cleanup
    [UpdateAfter(typeof(DestructionSystem))]
    public partial struct CleanupSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            var ecb = new EntityCommandBuffer(state.WorldUpdateAllocator);

            foreach (var (cleanup, entity) in SystemAPI.Query<ExampleCleanupComponent>().WithNone<ExampleTagComponent>().WithEntityAccess())
            {
                // Perform cleanup ...

                // Remove Cleanup Component. This triggers the destruction of the Entity.
                ecb.RemoveComponent<ExampleCleanupComponent>(entity);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();

            state.Enabled = false;
        }
    }


    #endregion

    #region tag
    public struct ExampleTagComponent : IComponentData
    {

    }
    #endregion

    #region unmanaged
    public struct ExampleUnmanagedComponent : IComponentData
    {
        public int Value;
    }
    #endregion

#if !UNITY_DISABLE_MANAGED_COMPONENTS
    #region managed
    public class ExampleManagedComponent : IComponentData
    {
        public int Value;
    }
    #endregion
#endif

    #region shared-unmanaged
    public struct ExampleUnmanagedSharedComponent : ISharedComponentData
    {
        public int Value;
    }
    #endregion

    #region shared-managed
    public struct ExampleManagedSharedComponent : ISharedComponentData, IEquatable<ExampleManagedSharedComponent>
    {
        public string Value; // A managed field type

        public bool Equals(ExampleManagedSharedComponent other)
        {
            return Value.Equals(other.Value);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }
    }
    #endregion
}
