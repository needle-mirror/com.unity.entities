using System;
using Unity.Transforms;
using UnityEngine;

namespace Unity.Entities.Streaming
{    
    [DisableAutoCreation]
    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    class OptimizationGroup : ComponentSystemGroup
    {
    }


    public static class EntitySceneOptimization
    {
        static void MarkStaticFrozen(EntityManager entityManager)
        {
            var staticGroup = entityManager.CreateComponentGroup(typeof(Static));
            entityManager.AddComponent(staticGroup, ComponentType.ReadWrite<Frozen>());
            staticGroup.Dispose();
        }

        static void RemoveSystemState(EntityManager entityManager)
        {
            foreach (var s in TypeManager.AllTypes)
            {
                if (TypeManager.IsSystemStateComponent(s.TypeIndex))
                {
                    //@TODO: Make query instead of this crazy slow shit
                    entityManager.RemoveComponent(entityManager.UniversalGroup, ComponentType.FromTypeIndex(s.TypeIndex));
                }
            }
            
        }


        public static void Optimize(World world)
        {
            var group = world.GetOrCreateManager<OptimizationGroup>();

            var systemTypes = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.EntitySceneOptimizations);
            foreach (var systemType in systemTypes)
                AddSystemAndLogException(world, group, systemType);
            group.SortSystemUpdateList();

            group.Update();

            var entityManager = world.GetOrCreateManager<EntityManager>();
            RemoveSystemState(entityManager);
            MarkStaticFrozen(entityManager);
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