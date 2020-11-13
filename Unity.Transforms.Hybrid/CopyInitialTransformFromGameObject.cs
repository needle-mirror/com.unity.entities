using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// Copy Transform from GameObject associated with Entity to TransformMatrix.
    /// Once only. Component is removed after copy.
    /// </summary>
    [GenerateAuthoringComponent]
    public struct CopyInitialTransformFromGameObject : IComponentData {}
}
