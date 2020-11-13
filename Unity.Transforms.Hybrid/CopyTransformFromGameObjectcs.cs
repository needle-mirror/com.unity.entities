using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// Copy Transform from GameObject associated with Entity to TransformMatrix.
    /// </summary>
    [WriteGroup(typeof(LocalToWorld))]
    [GenerateAuthoringComponent]
    public struct CopyTransformFromGameObject : IComponentData {}
}
