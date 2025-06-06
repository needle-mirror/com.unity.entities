using Unity.Entities;
using Unity.Scenes.Hybrid.Tests;
using UnityEngine;
using UnityObjectRefPlaymodeTests = Unity.Scenes.Hybrid.Tests.UnityObjectRefPlaymodeTests;

public class UnityObjectRefTestAuthoring : MonoBehaviour
{
    public ReferencedComponent referencedComponent;

    public class ReferencedComponentBaker : Baker<UnityObjectRefTestAuthoring>
    {
        public override void Bake(UnityObjectRefTestAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.None);
            AddComponent(e, new UnityObjectRefPlaymodeTests.UnityObjectRefWithComponent
            {
                UnityObjectRef =  authoring.referencedComponent
            });
        }

    }
}

