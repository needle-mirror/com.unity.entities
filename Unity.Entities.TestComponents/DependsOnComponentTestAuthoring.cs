using System.Collections.Generic;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AddComponentMenu("")]
    public class DependsOnComponentTestAuthoring : MonoBehaviour
    {
        public GameObject Other;
        public static Dictionary<GameObject, int> Versions = new Dictionary<GameObject,int>();

        class Baker : Baker<DependsOnComponentTestAuthoring>
        {
            public override void Bake(DependsOnComponentTestAuthoring authoring)
            {
                DependsOn(authoring.Other);

                if (authoring.Other == null || authoring.Other.scene != authoring.gameObject.scene)
                    AddComponent(new Component {Value = -1});
                else
                {
                    Versions.TryGetValue(authoring.Other, out var version);
                    AddComponent(new Component {Value = version});
                }
            }
        }

        public struct Component : IComponentData
        {
            public int Value;
        }
    }
}
