using Unity.Entities;

namespace Unity.Scenes.Editor.PlayerTests
{
    public struct EcsTestData : IComponentData
    {
        public int value;

        public EcsTestData(int value) => this.value = value;

        public override string ToString() => value.ToString();
    }
}
