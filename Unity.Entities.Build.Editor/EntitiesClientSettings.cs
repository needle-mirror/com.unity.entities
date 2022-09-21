using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Build.Editor
{
    internal class EntitiesClientSettings : DotsPlayerSettings
    {
        [SerializeField]
        public BakingSystemFilterSettings FilterSettings;

        public override BakingSystemFilterSettings GetFilterSettings()
        {
            return FilterSettings;
        }

        public override string[] GetAdditionalScriptingDefines()
        {
            return Array.Empty<string>();
        }
    }

    internal class ClientSettings : DotsPlayerSettingsProvider
    {
        private EntitiesClientSettings m_ClientSettings;
        private VisualElement m_rootElement;

        private Hash128 m_ClientGUID;

        public override int Importance
        {
            get{ return 0; }
        }

        public override DotsGlobalSettings.PlayerType GetPlayerType()
        {
            return DotsGlobalSettings.PlayerType.Client;
        }

        public override Hash128 GetPlayerSettingGUID()
        {
            if (!m_ClientGUID.IsValid)
                LoadOrCreateAsset();
            return m_ClientGUID;
        }

        public override DotsPlayerSettings GetSettingAsset()
        {
            if (m_ClientSettings == null)
                LoadOrCreateAsset();
            return m_ClientSettings;
        }

        void LoadOrCreateAsset()
        {
            var path = k_DefaultAssetPath + k_DefaultAssetName + "ClientSettings" + k_DefaultAssetExtension;
            if(File.Exists(path))
                m_ClientSettings = AssetDatabase.LoadAssetAtPath<EntitiesClientSettings>(path);
            else
            {
                m_ClientSettings = (EntitiesClientSettings)ScriptableObject.CreateInstance(typeof(EntitiesClientSettings));
                m_ClientSettings.name = k_DefaultAssetName + nameof(EntitiesClientSettings);

                AssetDatabase.CreateAsset(m_ClientSettings, path);
            }
            m_ClientGUID = new Hash128(AssetDatabase.AssetPathToGUID(path));
        }

        public override void Enable(int value)
        {
            m_rootElement.SetEnabled((value == (int)DotsGlobalSettings.PlayerType.Client));
        }

        public override void OnActivate(DotsGlobalSettings.PlayerType type, VisualElement rootElement)
        {
            m_rootElement = new VisualElement();
            m_rootElement.AddToClassList("target");

            var so = new SerializedObject(GetSettingAsset());
            m_rootElement.Bind(so);
            so.Update();

            var label = new Label("Client");
            m_rootElement.Add(label);

            var targetS = new VisualElement();
            targetS.AddToClassList("target-Settings");

            targetS.Add(new PropertyField(so.FindProperty("FilterSettings").FindPropertyRelative("ExcludedBakingSystemAssemblies")));

            m_rootElement.Add(targetS);

            rootElement.Add(m_rootElement);
            so.ApplyModifiedProperties();
        }

        public override string[] GetExtraScriptingDefines()
        {
            return Array.Empty<string>();
        }
    }
}
