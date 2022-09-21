using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    // We need a custom type because `isSelectable` is not available through UXML
    class SelectableLabel : Label
    {
        new class UxmlFactory : UxmlFactory<SelectableLabel, UxmlTraits> { }
        new class UxmlTraits : Label.UxmlTraits { }

        public SelectableLabel() : this(string.Empty) { }
        public SelectableLabel(string text) : base(text) => ((ITextSelection)this).isSelectable = true;
    }
}
