using System.Collections.Generic;
using Unity.Properties;
using Unity.Properties.UI;
using Unity.Scenes;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    sealed class BufferElement<TList, TElement> : ComponentElementBase
        where TList : IList<TElement>
    {
        readonly PropertyElement m_Content;

        public BufferElement(IComponentProperty property, EntityInspectorContext context, ref InspectedBuffer<TList, TElement> value) : base(property, context)
        {
            m_Content = CreateContent(property, ref value);
        }

        protected override void OnComponentChanged(PropertyElement element, PropertyPath path)
        {
            var buffer = element.GetTarget<InspectedBuffer<TList, TElement>>();
            var container = Container;

            if (container.IsReadOnly)
                return;

            PropertyContainer.SetValue(ref container, Path, buffer.Value);
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();
        }

        protected override void SetReadonly(VisualElement root)
        {
            // Buffers will generate a paginated list of elements. In read-only mode, we will selectively disable
            // specific elements to keep navigation enabled. This will only work for the top-level list. Nested paginated
            // lists will be completely disabled.
            root.Q(className: "unity-properties__list-element__size")?.SetEnabled(false);
            root.Q("properties-list-content")?.SetEnabled(false);
            root.Q<Button>(className: "unity-properties__list-element__add-item-button")?.SetEnabled(false);
        }

        protected override void OnPopulateMenu(DropdownMenu menu)
        {
            var buffer = m_Content.GetTarget<InspectedBuffer<TList, TElement>>();
            menu.AddCopyValue(buffer.Value);
        }
    }
}
