using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Entities.Build.Editor
{
    internal class DotsGlobalSettingsProvider : SettingsProvider
    {
        private DotsGlobalSettingsProvider(string path, SettingsScope scopes)
            : base(path, scopes)
        {
            label = "DOTS";
            keywords = GetSearchKeywordsFromSerializedObject(new SerializedObject(DotsGlobalSettings.Instance.GetClientSettingAsset()));
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            //TODO: We should be using new SettingsWindow.GUIScope() here to respect same style than other project settings tabs but this require a trunk change to have internal visibility to the class
            var instance = DotsGlobalSettings.Instance;

            StyleSheet stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.entities/Unity.Entities.Build.Editor/Resources/StyleSheets/dots-settings-window.uss");
            rootElement.styleSheets.Add(stylesheet);
            rootElement.AddToClassList("sb-settings-window");

            var title = new Label("DOTS");
            title.AddToClassList("title");
            rootElement.Add(title);

            var so = new SerializedObject(instance);
            var element = new VisualElement();
            element.Bind(so);
            element.AddToClassList("body");
            so.Update();

            if (instance.ServerProvider != null)
            {
                var enumField = new EnumField("Player type:", instance.GetPlayerType());
                enumField.tooltip = L10n.Tr("Select the dots player type to build, Client or Server");
                enumField.RegisterCallback<ChangeEvent<Enum>>(evt =>
                {
                    for (int i = 0; i < instance.Providers.Count; i++)
                    {
                        var extraSetting = instance.Providers[i];
                        extraSetting.Enable((int)((DotsGlobalSettings.PlayerType)evt.newValue));
                    }
                    instance.SetPlayerType((DotsGlobalSettings.PlayerType)evt.newValue);
                });
                element.Add(enumField);
            }

            // Activate providers
            for (int i = 0; i < instance.Providers.Count; i++)
            {
                var extraSetting = instance.Providers[i];
                extraSetting.OnActivate(instance.GetPlayerType(), element);
            }
            so.ApplyModifiedProperties();
            rootElement.Add(element);

            base.OnActivate(searchContext, rootElement);
        }

        [SettingsProvider]
        public static SettingsProvider CreateDotsGlobalSettingsProvider()
        {
            return new DotsGlobalSettingsProvider("Project/DOTS", SettingsScope.Project);
        }
    }
}

