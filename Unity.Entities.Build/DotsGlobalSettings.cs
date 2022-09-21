#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEngine;

namespace Unity.Entities.Build
{
    // Base class of a dots player asset
    public abstract class DotsPlayerSettings : ScriptableObject
    {
        public abstract BakingSystemFilterSettings GetFilterSettings();
        public abstract string[] GetAdditionalScriptingDefines();

        /// <summary>
        /// TEMP: This was only added to allow NetCode to inject baking systems into the scene asset importer.
        /// See NetCode's `TestWithSceneAsset.cs` script Setup and Teardown.
        /// </summary>
        internal static List<Type> AdditionalBakingSystemsTemp = new List<Type>();
    }

    // Base class for creating a dots player asset in the Assets folder.
    public abstract class DotsPlayerSettingsProvider
    {
        protected static string k_DefaultAssetName = "Dots";
        protected static string k_DefaultAssetPath = "Assets/"; //TODO serialize assets into the project settings folder instead once we can register them in the asset database
        protected static string k_DefaultAssetExtension = ".asset";

        // Importance of the asset compared with other potential assets of the same player type
        // The provider with the higher importance value only will be taking into account in the project settings
        public virtual int Importance
        {
            get { return 0; }
        }

        // Provides extra scripting defines to add into the player build
        public virtual string[] GetExtraScriptingDefines()
        {
            return Array.Empty<string>();
        }

        // Provides additional BuildOptions to the player build
        public virtual BuildOptions GetExtraBuildOptions()
        {
            return BuildOptions.None;
        }

        // Enable the UI of the asset in the Project settings window
        public abstract void Enable(int value);

        // Create the UI of the asset in the project settings window
        public abstract void OnActivate(DotsGlobalSettings.PlayerType type, VisualElement rootElement);

        // Returns the scriptable object of the asset
        public abstract DotsPlayerSettings GetSettingAsset();

        // Returns the GUID of the asset
        public abstract Hash128 GetPlayerSettingGUID();

        // Return the PlayerType of the asset (Client or Server)
        public abstract DotsGlobalSettings.PlayerType GetPlayerType();
    }

    // Base class for accessing Dots project settings
    public class DotsGlobalSettings : ScriptableObject
    {
        public enum PlayerType
        {
            Client,
            Server
        }

        public DotsPlayerSettingsProvider ClientProvider;
        public DotsPlayerSettingsProvider ServerProvider;

        static DotsGlobalSettings m_Instance;
        private const string m_EditorPrefsPlayerType = "com.unity.entities.playertype";
        private PlayerType m_Type;
        internal List<DotsPlayerSettingsProvider> Providers = new List<DotsPlayerSettingsProvider>();

        // Returns the PlayerType Client or Server registered in the Editor preferences currently set in the project settings window
        public PlayerType GetPlayerType()
        {
            return EditorPrefs.GetInt(m_EditorPrefsPlayerType, 0) == 0 ? PlayerType.Client : PlayerType.Server;
        }

        public void SetPlayerType(PlayerType value)
        {
            m_Type = value;
            EditorPrefs.SetInt(m_EditorPrefsPlayerType, m_Type == PlayerType.Client ? 0 : 1);
        }

        public static DotsGlobalSettings Instance {
            get
            {
                if (m_Instance == null)
                    m_Instance = ScriptableObject.CreateInstance<DotsGlobalSettings>();
                m_Instance.AddExtraDotsSettings();
                return m_Instance;
            }
        }

        void AddExtraDotsSettings()
        {
            m_Instance.Providers = new List<DotsPlayerSettingsProvider>();

            var types = TypeCache.GetTypesDerivedFrom<DotsPlayerSettingsProvider>();

            if (ClientProvider == null)
            {
                foreach (var type in types)
                {
                    var instance = (DotsPlayerSettingsProvider) Activator.CreateInstance(type);
                    if (instance.GetPlayerType() == PlayerType.Client)
                    {
                        if(ClientProvider == null)
                            ClientProvider = instance;
                        else if (instance.Importance > ClientProvider.Importance)
                            ClientProvider = instance;
                    }
                }
            }
            if (ClientProvider != null)
            {
                Providers.Add(ClientProvider);
            }

            if (ServerProvider == null)
            {
                foreach (var type in types)
                {
                    var instance = (DotsPlayerSettingsProvider) Activator.CreateInstance(type);
                    if(instance.GetPlayerType() == PlayerType.Server)
                    {
                        if(ServerProvider == null)
                            ServerProvider = instance;
                        else if (instance.Importance > ServerProvider.Importance)
                            ServerProvider = instance;
                    }
                }
            }
            if (ServerProvider != null)
            {
                Providers.Add(ServerProvider);
            }
        }

        // Returns the Client Setting asset
        public DotsPlayerSettings GetClientSettingAsset()
        {
            return ClientProvider?.GetSettingAsset();
        }

        // Returns the GUID of the Client asset
        public Hash128 GetClientGUID()
        {
            if (ClientProvider == null)
            {
                return default;
            }
            return ClientProvider.GetPlayerSettingGUID();
        }

        // Returns the Server Setting asset
        public DotsPlayerSettings GetServerSettingAsset()
        {
            return ServerProvider?.GetSettingAsset();
        }

        // Returns the GUID of the Server asset
        public Hash128 GetServerGUID()
        {
            if (ServerProvider == null)
                return default;
            return ServerProvider.GetPlayerSettingGUID();
        }
    }
}
#endif
