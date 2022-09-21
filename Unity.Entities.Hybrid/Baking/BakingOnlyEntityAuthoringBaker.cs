using Unity.Collections;
using UnityEngine;
using Unity.Entities;

namespace Unity.Entities.Hybrid.Baking
{
    internal class BakingOnlyEntityAuthoringBaker : Baker<BakingOnlyEntityAuthoring>
    {

        [TemporaryBakingType]
        public struct BakingOnlyChildren : IBufferElementData
        {
            public Entity entity;
        }

        public override void Bake(BakingOnlyEntityAuthoring authoring)
        {
            AddComponent<BakingOnlyEntity>();
            var childrenBuffer = AddBuffer<BakingOnlyChildren>();

            foreach (var childGameObject in GetChildren(true))
            {
                childrenBuffer.Add(new BakingOnlyChildren() {entity = GetEntity(childGameObject)});
            }
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
    partial class BakingOnlyEntityAuthoringBakingSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            var ecbP = ecb.AsParallelWriter();

            Entities
                .ForEach((int nativeThreadIndex, in DynamicBuffer<BakingOnlyEntityAuthoringBaker.BakingOnlyChildren> childrenBuffer) =>
                {
                    foreach (var child in childrenBuffer)
                    {
                        ecbP.AddComponent<BakingOnlyEntity>(nativeThreadIndex, child.entity);
                    }
                }).ScheduleParallel();

            CompleteDependency();
            ecb.Playback(EntityManager);
            ecb.Dispose();
        }
    }
}
