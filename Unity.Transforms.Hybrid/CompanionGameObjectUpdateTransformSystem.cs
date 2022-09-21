#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Burst;
using Unity.Collections;
using Unity.Transforms;
using UnityEngine.Jobs;

namespace Unity.Entities
{
    struct CompanionGameObjectUpdateTransformCleanup : ICleanupComponentData
    {
    }

    [WorldSystemFilter(WorldSystemFilterFlags.Default | WorldSystemFilterFlags.Editor)]
    [UpdateAfter(typeof(TransformSystemGroup))]
    [BurstCompile]
    internal partial class CompanionGameObjectUpdateTransformSystem : SystemBase
    {
        TransformAccessArray m_TransformAccessArray;
        NativeList<Entity> m_Entities;
        NativeHashMap<Entity, int> m_EntitiesMap;

        EntityQuery m_CreatedQuery;
        EntityQuery m_DestroyedQuery;

        protected override void OnCreate()
        {
            m_TransformAccessArray = new TransformAccessArray(0);
            m_Entities = new NativeList<Entity>(64, Allocator.Persistent);
            m_EntitiesMap = new NativeHashMap<Entity, int>(64, Allocator.Persistent);
            m_CreatedQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<CompanionLink>()},
                    None = new[] {ComponentType.ReadOnly<CompanionGameObjectUpdateTransformCleanup>()}
                }
            );
            m_DestroyedQuery = GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<CompanionGameObjectUpdateTransformCleanup>()},
                    None = new[] {ComponentType.ReadOnly<CompanionLink>()}
                }
            );
        }

        protected override void OnDestroy()
        {
            m_TransformAccessArray.Dispose();
            m_Entities.Dispose();
            m_EntitiesMap.Dispose();
        }

        struct RemoveDestroyedEntitiesArgs
        {
            public EntityQuery DestroyedQuery;
            public NativeList<Entity> Entities;
            public NativeHashMap<Entity, int> EntitiesMap;
            public TransformAccessArray TransformAccessArray;
            public EntityManager EntityManager;
        }

        [BurstCompile]
        static void RemoveDestroyedEntities(ref RemoveDestroyedEntitiesArgs args)
        {
            var entities = args.DestroyedQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                // This check is necessary because the code for adding entities is conditional and in edge-cases where
                // objects are quickly created-and-destroyed, we might not have the entity in the map.
                if (args.EntitiesMap.TryGetValue(entity, out var index))
                {
                    args.TransformAccessArray.RemoveAtSwapBack(index);
                    args.Entities.RemoveAtSwapBack(index);
                    args.EntitiesMap.Remove(entity);
                    if (index < args.Entities.Length)
                    {
                        args.EntitiesMap[args.Entities[index]] = index;
                    }
                }
            }
            entities.Dispose();
            args.EntityManager.RemoveComponent<CompanionGameObjectUpdateTransformCleanup>(args.DestroyedQuery);
        }

        protected override void OnUpdate()
        {
            if (!m_CreatedQuery.IsEmpty)
            {
                var entities = m_CreatedQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var link = EntityManager.GetComponentData<CompanionLink>(entity);
                    // It is possible that an object is created and immediately destroyed, and then this shouldn't run.
                    if (link.Companion != null)
                    {
                        int index = m_Entities.Length;
                        m_EntitiesMap.Add(entity, index);
                        m_TransformAccessArray.Add(link.Companion.transform);
                        m_Entities.Add(entity);
                    }
                }
                entities.Dispose();
                EntityManager.AddComponent<CompanionGameObjectUpdateTransformCleanup>(m_CreatedQuery);
            }

            if (!m_DestroyedQuery.IsEmpty)
            {
                var args = new RemoveDestroyedEntitiesArgs
                {
                    Entities = m_Entities,
                    DestroyedQuery = m_DestroyedQuery,
                    EntitiesMap = m_EntitiesMap,
                    EntityManager = EntityManager,
                    TransformAccessArray = m_TransformAccessArray
                };
                RemoveDestroyedEntities(ref args);
            }

            Dependency = new CopyTransformJob
            {
                localToWorld = GetComponentLookup<LocalToWorld>(),
                entities = m_Entities
            }.Schedule(m_TransformAccessArray, Dependency);
        }

        [BurstCompile]
        struct CopyTransformJob : IJobParallelForTransform
        {
            [NativeDisableParallelForRestriction] public ComponentLookup<LocalToWorld> localToWorld;
            [ReadOnly] public NativeList<Entity> entities;

            public unsafe void Execute(int index, TransformAccess transform)
            {
                var ltw = localToWorld[entities[index]];
                var mat = *(UnityEngine.Matrix4x4*) &ltw;
                transform.localPosition = ltw.Position;
                transform.localRotation = mat.rotation;
                transform.localScale = mat.lossyScale;
            }
        }
    }
}
#endif
