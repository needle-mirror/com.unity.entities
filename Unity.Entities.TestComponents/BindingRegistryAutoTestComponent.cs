namespace Unity.Entities.Tests
{
    public struct BindingRegistryAutoTestComponent : IComponentData
    {
        public float BindFloat;
        public int BindInt;
        public bool BindBool;
    }

    public class BindingRegistryAutoTestComponentAuthoring : UnityEngine.MonoBehaviour
    {
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryAutoTestComponent), "BindFloat")]
        public float BindFloat;
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryAutoTestComponent), "BindInt")]
        public int BindInt;
        [Unity.Entities.RegisterBinding(typeof(BindingRegistryAutoTestComponent), "BindBool")]
        public bool BindBool;


        class BindingRegistryAutoTestComponentBaker : Unity.Entities.Baker<BindingRegistryAutoTestComponentAuthoring>
        {
            public override void Bake(BindingRegistryAutoTestComponentAuthoring authoring)
            {
                Unity.Entities.Tests.BindingRegistryAutoTestComponent component = default(Unity.Entities.Tests.BindingRegistryAutoTestComponent);
                component.BindFloat = authoring.BindFloat;
                component.BindInt = authoring.BindInt;
                component.BindBool = authoring.BindBool;
                AddComponent(component);
            }
        }
    }
}
