using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class TabContent : VisualElement, ITabContent, INotifyValueChanged<string>
    {
        static readonly string s_UssClassName = "tab-element";
        string m_TabName;

        public string TabName
        {
            get => m_TabName;
            set
            {
                using var pooled = ChangeEvent<string>.GetPooled(m_TabName, value);
                pooled.target = this;
                SetValueWithoutNotify(value);
                SendEvent(pooled);
            }
        }

        public virtual void OnTabVisibilityChanged(bool isVisible) { }

        public TabContent()
        {
            AddToClassList(s_UssClassName);
        }

        public void SetValueWithoutNotify(string newValue)
            => m_TabName = newValue;

        string INotifyValueChanged<string>.value
        {
            get => TabName;
            set => TabName = value;
        }
    }
}
