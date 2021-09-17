#if ENABLE_ACTIVE_BUILD_CONFIG
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Build;
using Unity.Build.Classic;
using Unity.Build.Common;
using Unity.Entities.Editor.UIElements;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ActiveBuildConfigurationPopup : EditorWindow
    {
        public const int ItemHeight = 20;
        public const int MaxItems = 15;
        static readonly string s_Options = L10n.Tr("Play Options...");
        static readonly string s_Create = L10n.Tr("Create Build Configuration...");
        static readonly VisualElementTemplate s_PopupTemplate = PackageResources.LoadTemplate("ActiveBuildConfiguration/active-build-config-popup");

        SearchField m_SearchField;
        ListView m_ListView;
        VisualElement m_Create;
        VisualElement m_Options;
        List<BuildConfiguration> m_Items;

        public static ActiveBuildConfigurationPopup Get() => HasOpenInstances<ActiveBuildConfigurationPopup>() ? GetWindow<ActiveBuildConfigurationPopup>() : null;
        public static ActiveBuildConfigurationPopup GetOrCreate() => Get() ?? CreateInstance<ActiveBuildConfigurationPopup>();

        public List<BuildConfiguration> Items
        {
            get => m_Items;
            set
            {
                m_Items = value;
                m_ListView.itemsSource = value;
            }
        }

        void OnEnable()
        {
            ActiveBuildConfigurationDropdown.Instance.ShowTooltip = false;

            var popup = s_PopupTemplate.Clone();
            var anyConfigs = ActiveBuildConfiguration.GetBuildConfigurations().Any();
            m_SearchField = popup.Q<SearchField>("search");
            m_SearchField.RegisterValueChangedCallback(e =>
            {
                var visibleConfigs = ActiveBuildConfiguration.GetVisibleBuildConfigurations();
                Items = ActiveBuildConfiguration.GetMatchingBuildConfigurations(visibleConfigs, m_SearchField.value).ToList();
            });
            m_SearchField.SetVisibility(anyConfigs);

            m_ListView = popup.Q<ListView>("list");
            m_ListView.selectionType = SelectionType.Single;
            m_ListView.itemHeight = ItemHeight;
            m_ListView.makeItem = () =>
            {
                return new ActiveBuildConfigurationPopupItem();
            };
            m_ListView.bindItem = (element, index) =>
            {
                var item = element as ActiveBuildConfigurationPopupItem;
                item.BuildConfiguration = m_Items[index];
            };
            m_ListView.onSelectionChange += (items) =>
            {
                var config = items.First() as BuildConfiguration;
                EditorApplication.delayCall += () =>
                    ActiveBuildConfiguration.Current = config;
                Close();
            };
            m_ListView.SetVisibility(anyConfigs);

            m_Options = popup.Q("options");
            m_Options.Q<TextElement>("options-label").text = s_Options;
            m_Options.RegisterCallback<ClickEvent>(e =>
            {
                ActiveBuildConfigurationWindow.GetOrCreate().Show();
                e.StopPropagation();
            });
            m_Options.SetVisibility(anyConfigs);

            m_Create = popup.Q("create");
            m_Create.Q<TextElement>("create-label").text = s_Create;
            m_Create.RegisterCallback<ClickEvent>(e =>
            {
                BuildConfiguration.CreateAssetInActiveDirectory($"DefaultBuildConfiguration{BuildConfiguration.AssetExtension}", config =>
                {
                    config.SetComponent<GeneralSettings>();
                    config.SetComponent<SceneList>();

                    //@TODO: Eventually, we might want to choose a different build
                    //profile, for example when DOTS Runtime packages are installed.
                    config.SetComponent<ClassicBuildProfile>();
                });
                e.StopPropagation();
            });
            m_Create.SetVisibility(!anyConfigs);

            rootVisualElement.Add(popup);
        }

        void OnFocus() => m_SearchField.Focus();

        void OnLostFocus() => Close();

        void OnDestroy() => ActiveBuildConfigurationDropdown.Instance.ShowTooltip = true;
    }
}
#endif
