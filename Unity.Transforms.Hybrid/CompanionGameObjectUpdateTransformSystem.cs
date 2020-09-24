#if !UNITY_DISABLE_MANAGED_COMPONENTS
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using UnityEngine.Jobs;

struct CompanionGameObjectUpdateTransformSystemState : ISystemStateComponentData
{
}

[UnityEngine.ExecuteAlways]
[UpdateAfter(typeof(TransformSystemGroup))]
public class CompanionGameObjectUpdateTransformSystem : JobComponentSystem
{
    TransformAccessArray m_TransformAccessArray;

    EntityQuery m_NewQuery;
    EntityQuery m_ExistingQuery;
    EntityQuery m_DestroyedQuery;

    protected override void OnCreate()
    {
        m_TransformAccessArray = new TransformAccessArray(0);

        m_NewQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<CompanionLink>()},
                None = new[] {ComponentType.ReadOnly<CompanionGameObjectUpdateTransformSystemState>()}
            }
        );

        m_ExistingQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<CompanionLink>(),
                    ComponentType.ReadOnly<CompanionGameObjectUpdateTransformSystemState>(),
                    ComponentType.ReadOnly<LocalToWorld>(),
                }
            }
        );

        m_DestroyedQuery = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new[] {ComponentType.ReadOnly<CompanionGameObjectUpdateTransformSystemState>()},
                None = new[] {ComponentType.ReadOnly<CompanionLink>()}
            }
        );
    }

    protected override void OnDestroy()
    {
        m_TransformAccessArray.Dispose();
    }

    protected override unsafe JobHandle OnUpdate(JobHandle inputDeps)
    {
        EntityManager.AddComponent<CompanionGameObjectUpdateTransformSystemState>(m_NewQuery);
        EntityManager.RemoveComponent<CompanionGameObjectUpdateTransformSystemState>(m_DestroyedQuery);

        var entities = m_ExistingQuery.ToEntityArray(Allocator.Persistent);

        var transforms = new UnityEngine.Transform[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            var link = EntityManager.GetComponentData<CompanionLink>(entities[i]);
            transforms[i] = link.Companion.transform;
        }

        m_TransformAccessArray.SetTransforms(transforms);

        return new CopyTransformJob
        {
            localToWorld = GetComponentDataFromEntity<LocalToWorld>(),
            entities = entities
        }.Schedule(m_TransformAccessArray, inputDeps);
    }

    [BurstCompile]
    struct CopyTransformJob : IJobParallelForTransform
    {
        [NativeDisableParallelForRestriction] public ComponentDataFromEntity<LocalToWorld> localToWorld;

        [DeallocateOnJobCompletion]
        [ReadOnly] public NativeArray<Entity> entities;

        public unsafe void Execute(int index, TransformAccess transform)
        {
            var ltw = localToWorld[entities[index]];
            var mat = *(UnityEngine.Matrix4x4*) & ltw;
            transform.localPosition = ltw.Position;
            transform.localRotation = mat.rotation;
            transform.localScale = mat.lossyScale;
        }
    }
}
#endif
