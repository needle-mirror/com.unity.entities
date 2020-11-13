using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// Mark an additional entity with this tag component to instruct the incremental transform conversion system to
    /// copy the transform data from the primary entity. This allows you to copy data to additional entities without
    /// having to specify a dependency.
    /// </summary>
    public struct CopyTransformFromPrimaryEntityTag : IComponentData {}

    [WorldSystemFilter(WorldSystemFilterFlags.EntitySceneOptimizations)]
    class RemoveTransformCopyTag : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager.RemoveComponent<CopyTransformFromPrimaryEntityTag>(EntityManager.UniversalQuery);
        }
    }
}
