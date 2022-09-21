using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class TestComponentAuthoringIsBuildingForEditor : MonoBehaviour
    {
    }

    class TestComponentAuthoringIsBuildingForEditorBaker : Baker<TestComponentAuthoringIsBuildingForEditor>
    {
        public override void Bake(TestComponentAuthoringIsBuildingForEditor authoring)
        {
#if UNITY_EDITOR
            if (IsBakingForEditor())
                AddComponent(new IntTestData(1));
            else
                AddComponent(new IntTestData(2));
#else
            AddComponent(new IntTestData(3));
#endif
        }
    }
}
