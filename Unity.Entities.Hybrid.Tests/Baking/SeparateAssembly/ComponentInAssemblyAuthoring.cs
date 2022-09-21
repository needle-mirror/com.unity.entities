using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace Unity.Entities.Hybrid.Tests.Baking.SeparateAssembly
{
    public class ComponentInAssemblyAuthoring : MonoBehaviour
    {
        public int value;
    }

    public struct ComponentInAssemblyComponent : IComponentData
    {
        public int value;
    }

    public class ComponentInAssemblyAuthoringBaker : Baker<ComponentInAssemblyAuthoring>
    {
        public override void Bake(ComponentInAssemblyAuthoring authoring)
        {
            AddComponent(new ComponentInAssemblyComponent
            {
                value = authoring.value
            });
        }
    }
}
