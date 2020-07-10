using Unity.Entities;

[GenerateAuthoringComponent]
public struct SubSceneSectionTestData : IComponentData
{
    public SubSceneSectionTestData(int value)
    {
        Value = value;
    }

    public int Value;
}
