#if ENABLE_ACTIVE_BUILD_CONFIG
using System.Collections.Generic;
using System.Linq;
using Unity.Build;
using Unity.Build.Editor;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ActiveBuildConfigurationWindowItem : VisualElement
    {
        static readonly VisualElementTemplate s_WindowItemTemplate = PackageResources.LoadTemplate("ActiveBuildConfiguration/active-build-config-window-item");

        BuildConfiguration m_BuildConfiguration;
        readonly Image m_Icon;
        readonly TextElement m_Name;
        readonly Toggle m_Show;

        public BuildConfiguration BuildConfiguration
        {
            get => m_BuildConfiguration;
            set
            {
                m_BuildConfiguration = value;
                Update();
            }
        }

        public ActiveBuildConfigurationWindowItem()
        {
            var item = s_WindowItemTemplate.Clone();
            m_Icon = item.Q<Image>("icon");
            m_Name = item.Q<TextElement>("name");
            m_Show = item.Q<Toggle>("show");
            m_Show.RegisterCallback<ChangeEvent<bool>>(e =>
            {
                var window = ActiveBuildConfigurationWindow.Get();
                if (window == null)
                    return;

                if (m_BuildConfiguration.Show == e.newValue)
                    return;

                var configs = default(IEnumerable<BuildConfiguration>);
                if (window.SelectedItems.Contains(m_BuildConfiguration))
                    configs = window.SelectedItems;
                else
                    configs = new[] { m_BuildConfiguration };

                foreach (var config in configs)
                {
                    if (config == null || !config)
                        continue;

                    config.Show = e.newValue;
                    config.SerializeToPath(AssetDatabase.GetAssetPath(config));
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

                // Wait one frame to workaround assets not ready to load
                EditorApplication.delayCall += () => window.Refresh();
            });
            Add(item);
        }

        void Update()
        {
            var icon = m_BuildConfiguration.GetPlatform()?.GetIcon();
            if (m_Icon.image != icon)
                m_Icon.image = icon;

            var name = m_BuildConfiguration.name;
            if (m_Name.text != name)
                m_Name.text = name;

            var show = m_BuildConfiguration.Show;
            if (m_Show.value != show)
                m_Show.value = show;
        }
    }
}
#endif
