namespace Unity.Entities.Tests
{
    [GenerateAuthoringComponent]
    public struct BindingRegistryAutoTestComponent : IComponentData
    {
        public float BindFloat;
        public int BindInt;
        public bool BindBool;
    }
}
