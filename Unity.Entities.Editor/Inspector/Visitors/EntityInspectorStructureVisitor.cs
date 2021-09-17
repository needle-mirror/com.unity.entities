using Unity.Properties;

namespace Unity.Entities.Editor
{
    class EntityInspectorStructureVisitor : PropertyVisitor
    {
        public EntityInspectorComponentOrder ComponentOrder { get; }

        public EntityInspectorStructureVisitor()
        {
            ComponentOrder = new EntityInspectorComponentOrder();
        }

        public void Reset()
        {
            ComponentOrder.Reset();
        }

        protected override void VisitProperty<TContainer, TValue>(Property<TContainer, TValue> property,
            ref TContainer container, ref TValue value)
        {
            if (property is IComponentProperty componentProperty)
            {
                if (componentProperty.Type == ComponentPropertyType.Tag)
                    ComponentOrder.Tags.Add(componentProperty.Name);
                else
                    ComponentOrder.Components.Add(componentProperty.Name);
            }
        }

        protected override void VisitList<TContainer, TList, TElement>(Property<TContainer, TList> property,
            ref TContainer container, ref TList value)
        {
            if (property is IComponentProperty componentProperty)
            {
                ComponentOrder.Components.Add(componentProperty.Name);
            }
        }
    }
}
