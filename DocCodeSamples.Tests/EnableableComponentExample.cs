using Unity.Burst.Intrinsics;
using Unity.Entities;
using Unity.Collections;

namespace Doc.CodeSamples.Tests
{
    public struct Health : IComponentData, IEnableableComponent
    {
        public float Value;
    }

    public struct Translation : IComponentData, IEnableableComponent
    {

    }

    #region enableable-example
    public partial struct EnableableComponentSystem : ISystem
    {

        public void OnUpdate(ref SystemState system)
        {

            Entity e = system.EntityManager.CreateEntity(typeof(Health));

            ComponentLookup<Health> healthLookup = system.GetComponentLookup<Health>();

            // true
            bool b = healthLookup.IsComponentEnabled(e);

            // disable the Health component of the entity
            healthLookup.SetComponentEnabled(e, false);

            // though disabled, the component can still be read and modified
            Health h = healthLookup[e];

        }

    }
    #endregion

    #region enableable-health-example
    public partial struct EnableableHealthSystem : ISystem
    {
        public void OnUpdate(ref SystemState system)
        {
            Entity e1 = system.EntityManager.CreateEntity(typeof(Health), typeof(Translation));
            Entity e2 = system.EntityManager.CreateEntity(typeof(Health), typeof(Translation));

            // true (components begin life enabled)
            bool b = system.EntityManager.IsComponentEnabled<Health>(e1);

            // disable the Health component on the first entity
            system.EntityManager.SetComponentEnabled<Health>(e1, false);

            EntityQuery query = new EntityQueryBuilder(Allocator.Temp).WithAll<Health, Translation>().Build(ref system);

            // the returned array does not include the first entity
            var entities = query.ToEntityArray(Allocator.Temp);

            // the returned array does not include the Health of the first entity
            var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            // the returned array does not include the Translation of the first entity
            var translations = query.ToComponentDataArray<Translation>(Allocator.Temp);

            // This query matches components whether they're enabled or disabled
            var queryIgnoreEnableable = new EntityQueryBuilder(Allocator.Temp).WithAll<Health, Translation>().WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(ref system);

            // the returned array includes the Translations of both entities
            var translationsAll = queryIgnoreEnableable.ToComponentDataArray<Translation>(Allocator.Temp);
        }
    }
    #endregion

    public struct AliveTag : IComponentData, IEnableableComponent
    {
    }

    #region enableable-idiomaticforeach-example
    public partial struct EnableAliveFromHealthSystem : ISystem
    {
        public void OnUpdate(ref SystemState system)
        {
            foreach (var (health, aliveEnabled) in SystemAPI.Query<RefRO<Health>, EnabledRefRW<AliveTag>>())
            {
                // Disable the AliveTag component based on the Health component value.
                if (health.ValueRO.Value <= 0)
                    aliveEnabled.ValueRW = false;
            }
        }
    }
    #endregion

    #region enableable-ijobentity-example
    public partial struct EnableAliveFromHealthJob : IJobEntity
    {
        void Execute(in Health health, EnabledRefRW<AliveTag> aliveEnabled)
        {
            // Disable the AliveTag component based on the Health component value.
            if (health.Value <= 0)
                aliveEnabled.ValueRW = false;
        }
    }
    #endregion

    #region enableable-ijobchunk-example
    public struct EnableAliveFromHealthChunkJob : IJobChunk
    {
        [ReadOnly] public ComponentTypeHandle<Health> HealthTypeHandle;
        public ComponentTypeHandle<AliveTag> AliveTagTypeHandle;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            NativeArray<Health> chunkHealthValues = chunk.GetNativeArray(ref HealthTypeHandle);
            EnabledMask chunkAliveEnabledMask = chunk.GetEnabledMask(ref AliveTagTypeHandle);
            ChunkEntityEnumerator enumerator = new ChunkEntityEnumerator(useEnabledMask, chunkEnabledMask, chunk.Count);
            while(enumerator.NextEntityIndex(out var i))
            {
                // Disable the AliveTag component based on the Health component value.
                if (chunkHealthValues[i].Value <= 0)
                    chunkAliveEnabledMask[i] = false;
            }
        }
    }
    #endregion
}
