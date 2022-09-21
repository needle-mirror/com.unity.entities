using System;
using System.Reflection;
using JetBrains.Annotations;
using Unity.Properties;
using Unity.Platforms.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ComponentAttributes : ITabContent
    {
        static class Strings
        {
            public static readonly string TabName = L10n.Tr("Attributes");
        }

        public string TabName => Strings.TabName;
        public readonly Type ComponentType;

        public ComponentAttributes(Type componentType)
        {
            ComponentType = componentType;
        }

        public void OnTabVisibilityChanged(bool isVisible) { }
    }

    [UsedImplicitly]
    class ComponentAttributesInspector : PropertyInspector<ComponentAttributes>
    {
        static class Strings
        {
            public static readonly string MembersHeader         = L10n.Tr("Members");
            public static readonly string AttributesHeader      = L10n.Tr("Attributes");
            public static readonly string NamespaceLabel        = L10n.Tr("Namespace");
            public static readonly string EnableableLabel       = L10n.Tr("Is Enableable");
            public static readonly string BakingOnlyLabel       = L10n.Tr("Is Baking-Only Type");
            public static readonly string TempBakingLabel       = L10n.Tr("Is Temporary Baking Type");
            public static readonly string TypeIndexLabel        = L10n.Tr("Type Index");
            public static readonly string StableTypeHashLabel   = L10n.Tr("Stable Type Hash");
            public static readonly string TypeCategoryLabel     = L10n.Tr("Category");
            public static readonly string BufferCapacityLabel   = L10n.Tr("Buffer Capacity in Chunk");
            public static readonly string BufferOverheadLabel   = L10n.Tr("Buffer Overhead");
            public static readonly string TypeSizeLabel         = L10n.Tr("Type Size");
            public static readonly string SizeInChunkLabel      = L10n.Tr("Size in Chunk");
            public static readonly string AlignmentLabel        = L10n.Tr("Alignment");
            public static readonly string AlignmentInChunkLabel = L10n.Tr("Alignment in Chunk");
        }

        public override VisualElement Build()
        {
            var root = new VisualElement();

            var memberSection = new FoldoutWithoutActionButton {HeaderName = {text = Strings.MembersHeader}};
            var propertyBag = PropertyBag.GetPropertyBag(Target.ComponentType);

            // TODO: @sean how do we avoid this ?
            var method = typeof(ComponentAttributesInspector)
                .GetMethod(nameof(Visit), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(Target.ComponentType);
            method.Invoke(this, new object[] { propertyBag, memberSection });

            var typeInfo = TypeManager.GetTypeInfo(TypeManager.GetTypeIndex(Target.ComponentType));
            var attributesSection = new FoldoutWithoutActionButton { HeaderName = {text = Strings.AttributesHeader }};
            attributesSection.Add(new ComponentAttributeView(Strings.NamespaceLabel, Target.ComponentType.Namespace));

            attributesSection.Add(new ComponentAttributeView(Strings.EnableableLabel, TypeManager.IsEnableable(typeInfo.TypeIndex)));
            attributesSection.Add(new ComponentAttributeView(Strings.BakingOnlyLabel, typeInfo.BakingOnlyType));
            attributesSection.Add(new ComponentAttributeView(Strings.TempBakingLabel, typeInfo.TemporaryBakingType));

            attributesSection.Add(new ComponentAttributeView(Strings.TypeIndexLabel, typeInfo.TypeIndex.Value));
            attributesSection.Add(new ComponentAttributeView(Strings.StableTypeHashLabel, typeInfo.StableTypeHash));
            attributesSection.Add(new ComponentAttributeView(Strings.TypeCategoryLabel, typeInfo.Category.ToString()));

            if (typeInfo.Category == TypeManager.TypeCategory.BufferData)
            {
                var bufferHeaderSize = typeInfo.SizeInChunk - typeInfo.BufferCapacity * typeInfo.TypeSize;
                attributesSection.Add(new ComponentAttributeView(Strings.BufferCapacityLabel, typeInfo.BufferCapacity.ToString()));
                attributesSection.Add(new ComponentAttributeView(Strings.BufferOverheadLabel, FormattingUtility.BytesToString((ulong) bufferHeaderSize)));
            }

            attributesSection.Add(new ComponentAttributeView(Strings.TypeSizeLabel, FormattingUtility.BytesToString((ulong) typeInfo.TypeSize)));
            attributesSection.Add(new ComponentAttributeView(Strings.SizeInChunkLabel, FormattingUtility.BytesToString((ulong) typeInfo.SizeInChunk)));
            attributesSection.Add(new ComponentAttributeView(Strings.AlignmentLabel, FormattingUtility.BytesToString((ulong) typeInfo.AlignmentInBytes)));
            attributesSection.Add(new ComponentAttributeView(Strings.AlignmentInChunkLabel, FormattingUtility.BytesToString((ulong) typeInfo.AlignmentInChunkInBytes)));

            root.Add(memberSection);
            root.Add(attributesSection);
            return root;
        }

        void Visit<TContainer>(IPropertyBag<TContainer> propertyBag, VisualElement root)
        {
            foreach (var property in propertyBag.GetProperties())
            {
                root.Add(new ComponentAttributeView(property.Name, TypeUtility.GetTypeDisplayName(property.DeclaredValueType())));
            }
        }
    }

    class ComponentAttributeView : VisualElement
    {
        public ComponentAttributeView(string label, bool value) : this(label, value.ToString()) { }
        public ComponentAttributeView(string label, int value) : this(label, value.ToString()) { }
        public ComponentAttributeView(string label, ulong value) : this(label, value.ToString()) { }

        public ComponentAttributeView(string label, string value)
        {
            Resources.Templates.ContentProvider.ComponentAttribute.Clone(this);
            this.Q<Label>(className: UssClasses.ComponentAttribute.Name).text = label;
            this.Q<Label>(className: UssClasses.ComponentAttribute.Value).text = value;
        }
    }
}
