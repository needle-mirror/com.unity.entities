using UnityEngine.UIElements;

namespace Unity.Entities.Editor.UIElements
{
    class SearchField : TextField
    {
        internal new class UxmlFactory : UxmlFactory<SearchField, UxmlTraits> { }
        static VisualElementTemplate s_SearchFieldTemplate = PackageResources.LoadTemplate("ActiveBuildConfiguration/search-field");

        public SearchField()
        {
            LoadLayout();
        }

        public SearchField(string label) : base(label)
        {
            LoadLayout();
        }

        public SearchField(int maxLength, bool multiline, bool isPasswordField, char maskChar) : base(maxLength, multiline, isPasswordField, maskChar)
        {
            LoadLayout();
        }
        public SearchField(string label, int maxLength, bool multiline, bool isPasswordField, char maskChar) : base(label, maxLength, multiline, isPasswordField, maskChar)
        {
            LoadLayout();
        }

        void LoadLayout()
        {
            s_SearchFieldTemplate.Clone(this);
        }

        public new void Focus()
        {
            base.Focus();
            this.Q("unity-text-input").Focus();
        }
    }
}
