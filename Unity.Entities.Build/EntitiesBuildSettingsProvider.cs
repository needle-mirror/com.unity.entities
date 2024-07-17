using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Build
{
    internal class EntitiesBuildSettingsProvider : SettingsProvider
    {
        private EntitiesBuildSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
            label = "Build";
            keywords = GetSearchKeywordsFromSerializedObject(new SerializedObject(DotsGlobalSettings.Instance.GetClientSettingAsset().AsScriptableObject()));
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            var instance = DotsGlobalSettings.Instance;

            StyleSheet stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.entities/Unity.Entities.Build/StyleSheets/dots-settings-window.uss");
            rootElement.styleSheets.Add(stylesheet);
            rootElement.AddToClassList("sb-settings-window");

            var title = new Label("Build");
            title.AddToClassList("title");
            rootElement.Add(title);

            var subTitle = new Label("Entities");
            subTitle.AddToClassList("subtitle");
            rootElement.Add(subTitle);

            var element = new VisualElement();
            element.AddToClassList("body");

            // Activate providers
            foreach (var extraSetting in instance.Providers)
            {
                extraSetting.OnActivate(instance.GetPlayerType(), element);
            }
            rootElement.Add(element);

            base.OnActivate(searchContext, rootElement);
        }

        [SettingsProvider]
        public static SettingsProvider CreateDotsGlobalSettingsProvider()
        {
            var projectSettingsPath = "Project/Entities/Build";
            var instance = DotsGlobalSettings.Instance;
            if (instance.ServerProvider != null)
            {
                projectSettingsPath = instance.ServerProvider.ProviderPath;
            }
            return new EntitiesBuildSettingsProvider(projectSettingsPath, SettingsScope.Project);
        }
    }
}

