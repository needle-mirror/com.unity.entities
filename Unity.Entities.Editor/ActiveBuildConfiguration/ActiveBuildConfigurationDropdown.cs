#if ENABLE_ACTIVE_BUILD_CONFIG
using System;
using System.Linq;
using Unity.Build;
using Unity.Build.Editor;
using UnityEditor;
using UnityEngine;

#if UNITY_2021_1_OR_NEWER
using UnityEngine.UIElements;
#endif

namespace Unity.Entities.Editor
{
#if UNITY_2021_1_OR_NEWER
    class ActiveBuildConfigurationDropdown : VisualElement
#else
    class ActiveBuildConfigurationDropdown : PopupWindowContent
#endif
    {
        static Lazy<ActiveBuildConfigurationDropdown> s_Instance = new Lazy<ActiveBuildConfigurationDropdown>(() => new ActiveBuildConfigurationDropdown());
        static readonly string s_NoSelection = L10n.Tr("Select...");

        bool m_ShowTooltip = true;

        public static ActiveBuildConfigurationDropdown Instance => s_Instance.Value;

        public bool ShowTooltip
        {
            get => m_ShowTooltip;
            set
            {
                m_ShowTooltip = value;
                Update(ActiveBuildConfiguration.Current);
            }
        }

#if UNITY_2021_1_OR_NEWER
        static readonly Lazy<VisualElementTemplate> s_DropdownTemplate = new Lazy<VisualElementTemplate>(() => PackageResources.LoadTemplate("active-build-config-dropdown"));

        readonly VisualElement m_Input;
        readonly Image m_Icon;
        readonly TextElement m_Text;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.update += AddActiveBuildConfigurationDropdown;
            void AddActiveBuildConfigurationDropdown()
            {
                var root = ToolbarUtility.GetPlayModeToolbarRoot();
                if (root == null)
                    return;

                root.Insert(0, Instance);
                EditorApplication.update -= AddActiveBuildConfigurationDropdown;
            }
        }
#else
        Lazy<GUIContent> m_Content = new Lazy<GUIContent>(() => new GUIContent());
        Lazy<GUIStyle> m_Style = new Lazy<GUIStyle>(() => new GUIStyle("Dropdown")
        {
            fixedWidth = 96, // that's all the space we have :(
            margin = { top = 4, bottom = 4, left = 0, right = 0 },
        });

        public override void OnGUI(Rect rect) { }

        public void Draw()
        {
            var content = m_Content.Value;
            var style = m_Style.Value;
            var dropdownRect = GUILayoutUtility.GetRect(content, style);
            if (content.image != null)
                EditorGUIUtility.SetIconSize(new Vector2(16, 16));
            if (GUI.Button(dropdownRect, content, style))
                ShowPopup(dropdownRect);
        }
#endif

        ActiveBuildConfigurationDropdown()
        {
#if UNITY_2021_1_OR_NEWER
            var dropdown = s_DropdownTemplate.Value.Clone();
            m_Input = dropdown.Q("input");
            m_Input.RegisterCallback<MouseDownEvent>((e) =>
            {
                ShowPopup(dropdown.worldBound);
                e.StopPropagation();
            });
            m_Icon = m_Input.Q<Image>("icon");
            m_Text = m_Input.Q<TextElement>("text");

            AddToClassList("unity-editor-toolbar-element");
            Add(dropdown);
#endif

            BuildConfiguration.ActiveChanged += (e) => Update(e.NewValue);
            Update(ActiveBuildConfiguration.Current);
        }

        public void Update(BuildConfiguration config)
        {
            if (config == null || !config)
            {
#if UNITY_2021_1_OR_NEWER
                m_Icon.Hide();
                if (m_Text.text != s_NoSelection)
                    m_Text.text = s_NoSelection;
                if (m_Input.tooltip != null)
                    m_Input.tooltip = null;
#else
                var content = m_Content.Value;
                if (content.image != null)
                    content.image = null;
                if (content.text != s_NoSelection)
                    content.text = s_NoSelection;
                if (content.tooltip != null)
                    content.tooltip = null;
#endif
            }
            else
            {
                var name = config.name;
                var icon = config.GetPlatform()?.GetIcon();
                var tooltip = ShowTooltip ? name : null;

#if UNITY_2021_1_OR_NEWER
                m_Icon.SetVisibility(icon != null);
                if (m_Icon.image != icon)
                    m_Icon.image = icon;
                if (m_Text.text != name)
                    m_Text.text = name;
                if (m_Input.tooltip != tooltip)
                    m_Input.tooltip = tooltip;
#else
                var content = m_Content.Value;
                if (content.image != icon)
                    content.image = icon;
                if (content.text != name)
                    content.text = name;
                if (content.tooltip != tooltip)
                    content.tooltip = tooltip;
#endif
            }
        }

        void ShowPopup(Rect dropdownRect)
        {
            var height = /*borders*/2;
            var visibleConfigs = ActiveBuildConfiguration.GetVisibleBuildConfigurations().ToList();

            // We do not want to show "create build config" menu item if any
            // buildable config exist, regardless if they are visible or not.
            if (ActiveBuildConfiguration.GetBuildConfigurations().Any())
            {
                // Note: The popup will only ever show visible build configs.
                var maxCount = Mathf.Min(visibleConfigs.Count, ActiveBuildConfigurationPopup.MaxItems);
                height += /*search*/24 + (maxCount * ActiveBuildConfigurationPopup.ItemHeight) + /*items top-bottom*/2 + /*options*/20;
            }
            else
            {
                height += /*create*/20;
            }

            var popup = ActiveBuildConfigurationPopup.GetOrCreate();
            var screenRect = GUIUtility.GUIToScreenRect(dropdownRect);

            popup.Items = visibleConfigs;
            popup.position = new Rect(screenRect.x, screenRect.y + dropdownRect.height, 230, height);
            popup.ShowPopup();
        }
    }
}
#endif
