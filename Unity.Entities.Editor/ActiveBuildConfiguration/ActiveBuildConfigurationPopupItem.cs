#if ENABLE_ACTIVE_BUILD_CONFIG
using Unity.Build;
using Unity.Build.Editor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ActiveBuildConfigurationPopupItem : VisualElement
    {
        static readonly VisualElementTemplate s_PopupItemTemplate = PackageResources.LoadTemplate("ActiveBuildConfiguration/active-build-config-popup-item");

        BuildConfiguration m_BuildConfiguration;
        readonly Image m_Icon;
        readonly TextElement m_Name;

        public BuildConfiguration BuildConfiguration
        {
            get => m_BuildConfiguration;
            set
            {
                m_BuildConfiguration = value;
                Update();
            }
        }

        public ActiveBuildConfigurationPopupItem()
        {
            var item = s_PopupItemTemplate.Clone();
            m_Icon = item.Q<Image>("icon");
            m_Name = item.Q<TextElement>("name");
            Add(item);
        }

        void Update()
        {
            if (m_BuildConfiguration == null || !m_BuildConfiguration)
                return;

            var icon = m_BuildConfiguration.GetPlatform()?.GetIcon();
            if (m_Icon.image != icon)
                m_Icon.image = icon;

            var name = m_BuildConfiguration.name;
            if (m_Name.text != name)
                m_Name.text = name;
        }
    }
}
#endif
