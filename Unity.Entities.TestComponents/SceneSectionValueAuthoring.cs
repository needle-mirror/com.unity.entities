using System;
using UnityEngine;

namespace Unity.Entities.Tests
{
    public struct SceneSectionValue : IComponentData
    {
        public SceneSectionValue(int value)
        {
            Value = value;
        }

        public int Value;
    }

    public class SceneSectionValueAuthoring : MonoBehaviour
    {
        public int Value = -1;
    }
    class SceneSectionValueBaker : Baker<SceneSectionValueAuthoring>
    {
        public override void Bake(SceneSectionValueAuthoring authoring)
        {
            AddComponent(new SceneSectionValue(authoring.Value));
        }
    }
}
