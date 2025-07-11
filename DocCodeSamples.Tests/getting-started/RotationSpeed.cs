namespace Doc.CodeSamples.Tests.GettingStarted
{
    #region example
    using Unity.Entities;

    // This component defines the rotation speed of an entity.
    public struct RotationSpeed : IComponentData
    {
        public float RadiansPerSecond;
    }
    #endregion
}