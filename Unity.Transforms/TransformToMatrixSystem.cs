using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

#if !ENABLE_TRANSFORM_V1

namespace Unity.Transforms
{
    /// <summary>
    /// This system computes up-to-date local-to-world matrices for all entities with the <see cref="LocalToWorld"/> component.
    /// </summary>
    /// <remarks>
    /// The local-to-world matrix is computed directly from the entity's <see cref="LocalToWorldTransform"/>. If present,
    /// the optional <see cref="PostTransformMatrix"/> is multiplied into the final result; this can be used to implement non-affine
    /// transformation effects such as non-uniform scale.
    /// </remarks>
    [BurstCompile]
    [RequireMatchingQueriesForUpdate]
    [UpdateInGroup(typeof(TransformSystemGroup))]
    [UpdateAfter(typeof(TransformHierarchySystem))]
    public partial struct TransformToMatrixSystem : ISystem
    {
        private EntityQuery m_LocalToWorldQuery;
        private EntityQuery m_LocalToWorldPostTransformQuery;

        // LocalToWorldTransform
        [BurstCompile]
        partial struct TransformToMatrixJob : IJobEntity
        {
            void Execute(ref LocalToWorld localToWorld, in LocalToWorldTransform transform)
            {
                localToWorld.Value = transform.Value.ToMatrix();
            }
        }

        // LocalToWorldTransform * PostTransformMatrix
        [BurstCompile]
        partial struct LocalToWorldPostTransformToMatrixJob : IJobEntity
        {
            void Execute(ref LocalToWorld localToWorld, in LocalToWorldTransform transform,
                in PostTransformMatrix postTransformMatrix)
            {
                localToWorld.Value = math.mul(transform.Value.ToMatrix(), postTransformMatrix.Value);
            }
        }

        /// <inheritdoc cref="ISystem.OnUpdate"/>
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new TransformToMatrixJob().ScheduleParallel(m_LocalToWorldQuery);
            new LocalToWorldPostTransformToMatrixJob().ScheduleParallel(m_LocalToWorldPostTransformQuery);
        }

        /// <inheritdoc cref="ISystem.OnCreate"/>
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            // LocalToWorldTransform (without PostTransformMatrix)
            var builder = new EntityQueryBuilder(Allocator.Temp);
            builder.WithAllRW<LocalToWorld>();
            builder.WithAll<LocalToWorldTransform>();
            builder.WithNone<PostTransformMatrix>();
            builder.WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_LocalToWorldQuery = state.GetEntityQuery(builder);
            builder.Dispose();

            // LocalToWorldTransform (with PostTransformMatrix)
            builder = new EntityQueryBuilder(Allocator.Temp);
            builder.WithAllRW<LocalToWorld>();
            builder.WithAll<LocalToWorldTransform, PostTransformMatrix>();
            builder.WithOptions(EntityQueryOptions.FilterWriteGroup);
            m_LocalToWorldPostTransformQuery = state.GetEntityQuery(builder);
            builder.Dispose();
        }

        /// <inheritdoc cref="ISystem.OnDestroy"/>
        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
        }
    }
}

#endif
