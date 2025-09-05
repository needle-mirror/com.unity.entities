using JetBrains.Annotations;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.UI
{
    [UsedImplicitly]
    class ColorUsageAttributeInspector : BaseFieldAttributeInspector<ColorField, Color, ColorUsageAttribute>
    {
        public override VisualElement Build()
        {
            var element = base.Build();
            var usage = GetAttribute<ColorUsageAttribute>();
            m_Field.hdr = usage.hdr;
            m_Field.showAlpha = usage.showAlpha;
            return element;
        }
    }
}
