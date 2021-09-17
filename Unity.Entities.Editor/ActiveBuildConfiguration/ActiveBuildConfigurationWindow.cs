#if ENABLE_ACTIVE_BUILD_CONFIG
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Build;
using Unity.Entities.Editor.UIElements;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ActiveBuildConfigurationWindow : EditorWindow
    {
        public const int ItemHeight = 20;
        public static readonly string Title = L10n.Tr("Play Options");
        static readonly VisualElementTemplate s_WindowTemplate = PackageResources.LoadTemplate("ActiveBuildConfiguration/active-build-config-window");
        static readonly Lazy<Texture2D> s_PlayIcon = new Lazy<Texture2D>(() => EditorResources.LoadTexture<Texture2D>("Icons/PlayButton.png", true));

        SearchField m_SearchField;
        ListView m_ListView;
        List<BuildConfiguration> m_Items;

        public static ActiveBuildConfigurationWindow Get() => HasOpenInstances<ActiveBuildConfigurationWindow>() ? GetWindow<ActiveBuildConfigurationWindow>() : null;
        public static ActiveBuildConfigurationWindow GetOrCreate() => Get() ?? CreateInstance<ActiveBuildConfigurationWindow>();

        public List<BuildConfiguration> Items
        {
            get => m_Items;
            set
            {
                m_Items = value;
                m_ListView.itemsSource = value;
            }
        }

        public IEnumerable<BuildConfiguration> SelectedItems => m_ListView.selectedItems.Cast<BuildConfiguration>();

        void OnEnable()
        {
            titleContent = new GUIContent(Title, s_PlayIcon.Value);
            minSize = new Vector2(200, 200);

            var window = s_WindowTemplate.Clone();
            var leftPanel = window.Q("left-panel");
            m_SearchField = leftPanel.Q<SearchField>("search");
            m_SearchField.RegisterValueChangedCallback(e =>
            {
                Refresh();
            });

            m_ListView = leftPanel.Q<ListView>("list");
            m_ListView.selectionType = SelectionType.Multiple;
            m_ListView.itemHeight = ItemHeight;
            m_ListView.makeItem = () =>
            {
                return new ActiveBuildConfigurationWindowItem();
            };
            m_ListView.bindItem = (element, index) =>
            {
                var item = (ActiveBuildConfigurationWindowItem)element;
                item.BuildConfiguration = m_Items[index];
            };

            // Wait one frame to workaround assets not ready to load
            EditorApplication.delayCall += () => Refresh();

            rootVisualElement.Add(window);
        }

        public void Refresh()
        {
            var configs = ActiveBuildConfiguration.GetBuildConfigurations();
            Items = ActiveBuildConfiguration.GetMatchingBuildConfigurations(configs, m_SearchField.value).ToList();
        }
    }
}
#endif
