using Unity.Mathematics;

namespace Unity.Entities.Tests
{
    public struct BindingRegistryBoolComponent : IComponentData
    {
        public bool Bool1;
        public bool2 Bool2;
        public bool3 Bool3;
        public bool4 Bool4;
    }

    public class BindingRegistryBoolComponentAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool1")]
        public bool Bool1;
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool2.x", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool2.y", true)]
        public Unity.Mathematics.bool2 Bool2;
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool3.x", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool3.y", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool3.z", true)]
        public Unity.Mathematics.bool3 Bool3;
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool4.x", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool4.y", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool4.z", true)]
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryBoolComponent), "Bool4.w", true)]
        public Unity.Mathematics.bool4 Bool4;

        class BindingRegistryBoolComponentBaker : Unity.Entities.Baker<BindingRegistryBoolComponentAuthoring>
        {
            public override void Bake(BindingRegistryBoolComponentAuthoring authoring)
            {
                Unity.Entities.Tests.BindingRegistryBoolComponent component = default(Unity.Entities.Tests.BindingRegistryBoolComponent);
                component.Bool1 = authoring.Bool1;
                component.Bool2 = authoring.Bool2;
                component.Bool3 = authoring.Bool3;
                component.Bool4 = authoring.Bool4;
                AddComponent(component);
            }
        }
    }
}
