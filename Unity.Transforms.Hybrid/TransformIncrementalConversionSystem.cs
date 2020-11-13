#if UNITY_2020_2_OR_NEWER
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Conversion;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace Unity.Transforms
{
    [UpdateInGroup(typeof(ConversionSetupGroup))]
    [WorldSystemFilter(WorldSystemFilterFlags.GameObjectConversion)]
    [AlwaysUpdateSystem]
    class TransformIncrementalConversionSystem : SystemBase
    {
        private IncrementalChangesSystem m_Incremental;
        protected override void OnCreate()
        {
            base.OnCreate();
            m_Incremental = World.GetExistingSystem<IncrementalChangesSystem>();
            m_Incremental.DeclareComponentDependencyTracking<Transform>();
        }

        protected override void OnUpdate()
        {
            // Collect all instances for which a transform has been changed
            NativeList<int> localToWorldInstancesList = new NativeList<int>(Allocator.TempJob);
            m_Incremental.IncomingChanges.CollectGameObjectsWithComponentChange<Transform>(localToWorldInstancesList);
            if (localToWorldInstancesList.Length == 0)
            {
                localToWorldInstancesList.Dispose();
                return;
            }

            // Calculate the indices of all instances that we need to update the local-to-world on
            NativeHashMap<int, bool> localToWorldIndices = new NativeHashMap<int, bool>(0, Allocator.TempJob);
            var collectionJob = m_Incremental.SceneHierarchy.Hierarchy
                .CollectHierarchyInstanceIdsAndIndicesAsync(localToWorldInstancesList, localToWorldIndices);

            // Collect all dependents on the Transform components of the entities that we are going to patch
            bool success = m_Incremental.TryGetComponentDependencyTracker<Transform>(out var dependencyTracker);
            Assert.IsTrue(success);

            var dependents = new NativeList<int>(0, Allocator.TempJob);
            var dependenciesJob = dependencyTracker.CalculateDirectDependentsAsync(localToWorldInstancesList.AsDeferredJobArray(), dependents, collectionJob);
            dependenciesJob = localToWorldInstancesList.Dispose(dependenciesJob);
            m_Incremental.AddConversionRequest(dependents, dependenciesJob);

            // patch the changed transforms
            var dstEntityManager = m_Incremental.DstEntityManager;
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var patchingJob = new UpdateConvertedTransforms
            {
                Rotation = dstEntityManager.GetComponentDataFromEntity<Rotation>(),
                Translation = dstEntityManager.GetComponentDataFromEntity<Translation>(),
                LocalToWorld = dstEntityManager.GetComponentDataFromEntity<LocalToWorld>(),
                NonUniformScale = dstEntityManager.GetComponentDataFromEntity<NonUniformScale>(),
                CopyTransformFromRoot = dstEntityManager.GetComponentDataFromEntity<CopyTransformFromPrimaryEntityTag>(true),
                Parent = dstEntityManager.GetComponentDataFromEntity<Parent>(true),
                ConvertedEntities = m_Incremental.ConvertedEntities,
                Hierarchy = m_Incremental.SceneHierarchy.Hierarchy,
                ChangedIndices = localToWorldIndices,
                Ecb = ecb.AsParallelWriter()
            }.ScheduleReadOnly(m_Incremental.SceneHierarchy.TransformAccessArray, 64, collectionJob);

            localToWorldIndices.Dispose(patchingJob);

            patchingJob.Complete();
            ecb.Playback(dstEntityManager);
            ecb.Dispose();
        }

        [BurstCompile]
        struct UpdateConvertedTransforms : IJobParallelForTransform
        {
            [ReadOnly] public SceneHierarchy Hierarchy;
            [ReadOnly] public NativeHashMap<int, bool> ChangedIndices;

            [NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<LocalToWorld> LocalToWorld;

            [NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<Translation> Translation;

            [NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<Rotation> Rotation;

            [NativeDisableParallelForRestriction]
            public ComponentDataFromEntity<NonUniformScale> NonUniformScale;

            public EntityCommandBuffer.ParallelWriter Ecb;

            [ReadOnly] public ConvertedEntitiesAccessor ConvertedEntities;
            [ReadOnly] public ComponentDataFromEntity<CopyTransformFromPrimaryEntityTag> CopyTransformFromRoot;
            [ReadOnly] public ComponentDataFromEntity<Parent> Parent;

            public void Execute(int index, TransformAccess transform)
            {
                if (!ChangedIndices.TryGetValue(index, out var selfChanged))
                    return;
                int id = Hierarchy.GetInstanceIdForIndex(index);
                var iter = ConvertedEntities.GetEntities(id);
                bool isFirst = true;
                while (iter.MoveNext())
                {
                    var e = iter.Current;
                    if (!LocalToWorld.HasComponent(e))
                        continue;

                    // check whether this is a primary entity or needs to have its transform copied from the primary
                    if (!isFirst && !CopyTransformFromRoot.HasComponent(e))
                        continue;
                    isFirst = false;

                    LocalToWorld[e] = new LocalToWorld {Value = transform.localToWorldMatrix};
                    if (selfChanged)
                    {
                        if (!Translation.HasComponent(e))
                        {
                            // static entity
                            continue;
                        }

                        Translation[e] = new Translation {Value = transform.localPosition};
                        if (Rotation.HasComponent(e))
                            Rotation[e] = new Rotation {Value = transform.localRotation};

                        float3 scale;
                        if (Parent.HasComponent(e))
                            scale = CalculateLossyScale(transform.localToWorldMatrix, transform.rotation);
                        else
                            scale = transform.localScale;

                        if (math.any(scale != new float3(1)))
                        {
                            var scaleComponent = new NonUniformScale {Value = transform.localScale};
                            if (NonUniformScale.HasComponent(e))
                                NonUniformScale[e] = scaleComponent;
                            else
                                Ecb.AddComponent(index, e, scaleComponent);
                        }
                        else
                        {
                            if (NonUniformScale.HasComponent(e))
                                Ecb.RemoveComponent<NonUniformScale>(index, e);
                        }
                    }
                }
            }

            static float3 CalculateLossyScale(float4x4 matrix, quaternion rotation)
            {
                float4x4 m4x4 = matrix;
                float3x3 invR = new float3x3(math.conjugate(rotation));
                float3x3 gsm = new float3x3 { c0 = m4x4.c0.xyz, c1 = m4x4.c1.xyz, c2 = m4x4.c2.xyz };
                float3x3 scale = math.mul(invR, gsm);
                float3 globalScale = new float3(scale.c0.x, scale.c1.y, scale.c2.z);
                return globalScale;
            }
        }
    }
}
#endif
