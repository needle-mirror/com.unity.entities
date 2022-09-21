using Unity.Entities;
using Unity.Scenes.Hybrid.Tests;

[UnityEngine.DisallowMultipleComponent]
public class SubSceneSectionTestDataAuthoring : UnityEngine.MonoBehaviour
{
    [Unity.Entities.RegisterBinding(typeof(SubSceneSectionTestData), "Value")]
    public int Value;

    class SubSceneSectionTestDataBaker : Unity.Entities.Baker<SubSceneSectionTestDataAuthoring>
    {
        public override void Bake(SubSceneSectionTestDataAuthoring authoring)
        {
            SubSceneSectionTestData component = default(SubSceneSectionTestData);
            component.Value = authoring.Value;
            AddComponent(component);
        }
    }
}
