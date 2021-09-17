using System;
using System.Reflection;
using JetBrains.Annotations;
using Unity.Properties.Editor;
using Unity.Properties.Internal;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ComponentAttributes : ITabContent
    {
        public string TabName { get; } = L10n.Tr("Attributes");
        public readonly Type ComponentType;

        public ComponentAttributes(Type componentType)
        {
            ComponentType = componentType;
        }

        public void OnTabVisibilityChanged(bool isVisible) { }
    }

    [UsedImplicitly]
    class ComponentAttributesInspector : Inspector<ComponentAttributes>
    {
        public override VisualElement Build()
        {
            var root = new VisualElement();

            var memberSection = new FoldoutWithoutActionButton {HeaderName = {text = L10n.Tr("Members")}};
            var propertyBag = PropertyBagStore.GetPropertyBag(Target.ComponentType);

            // TODO: @sean how do we avoid this ?
            var method = typeof(ComponentAttributesInspector)
                .GetMethod(nameof(Visit), BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(Target.ComponentType);
            method.Invoke(this, new object[] { propertyBag, memberSection });

            var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(Target.ComponentType));
            var attributesSection = new FoldoutWithoutActionButton() { HeaderName = {text = L10n.Tr("Attributes") }};
            attributesSection.Add(new ComponentAttributeView("Namespace", Target.ComponentType.Namespace));
            attributesSection.Add(new ComponentAttributeView("Type Index", typeInfo.TypeIndex.ToString()));
            attributesSection.Add(new ComponentAttributeView("Stable Type Hash", typeInfo.StableTypeHash.ToString()));
            attributesSection.Add(new ComponentAttributeView("Category", typeInfo.Category.ToString()));
            attributesSection.Add(new ComponentAttributeView("Size in Chunk", $"{typeInfo.SizeInChunk} B"));
            if (typeInfo.Category == TypeManager.TypeCategory.BufferData)
            {
                var bufferHeaderSize = typeInfo.SizeInChunk - typeInfo.BufferCapacity * typeInfo.TypeSize;
                attributesSection.Add(new ComponentAttributeView("Buffer Capacity in Chunk", $"{typeInfo.BufferCapacity} B"));
                attributesSection.Add(new ComponentAttributeView("Buffer Overhead", $"{bufferHeaderSize} B"));
            }
            attributesSection.Add(new ComponentAttributeView("Type Size", $"{typeInfo.TypeSize} B"));
            attributesSection.Add(new ComponentAttributeView("Alignment", $"{typeInfo.AlignmentInBytes} B"));
            attributesSection.Add(new ComponentAttributeView("Alignment in Chunk", $"{typeInfo.AlignmentInChunkInBytes} B"));

            root.Add(memberSection);
            root.Add(attributesSection);
            return root;
        }

        void Visit<TContainer>(IPropertyBag<TContainer> propertyBag, VisualElement root)
        {
            if (propertyBag is IPropertyList<TContainer> propertyList)
            {
                TContainer container = default;
                foreach (var property in propertyList.GetProperties(ref container))
                {
                    root.Add(new ComponentAttributeView(property.Name, TypeUtility.GetTypeDisplayName(property.DeclaredValueType())));
                }
            }
        }
    }

    class ComponentAttributeView : VisualElement
    {
        public ComponentAttributeView(string label, string value)
        {
            Resources.Templates.ContentProvider.ComponentAttribute.Clone(this);
            this.Q<Label>(className: UssClasses.ComponentAttribute.Name).text = label;
            this.Q<Label>(className: UssClasses.ComponentAttribute.Value).text = value;
        }
    }
}
