using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Scenes.Editor
{
    public class ClearEntitiesCacheWindow : EditorWindow
    {
        ClearEntitiesCacheView m_View;
        static bool s_IsWindowVisible;

        public static void OpenWindow()
        {
            var wnd = GetWindow<ClearEntitiesCacheWindow>(true, "Clear Entities Cache(s)", true);
            if (s_IsWindowVisible)
                return;

            wnd.maxSize = new Vector2(500, 400);
            wnd.minSize = new Vector2(300, 200);
            wnd.Show();
            s_IsWindowVisible = true;
        }

        void OnEnable()
        {
            m_View = new ClearEntitiesCacheView();

            m_View.Initialize(this, rootVisualElement);
        }

        void OnDisable()
        {
            if (m_View == null)
                return;
            s_IsWindowVisible = false;

            m_View = null;
        }

        internal class ClearEntitiesCacheView
        {
            VisualElement m_Root;
            Button m_CancelButton;
            Button m_ClearButton;

            Toggle m_EntitySceneCacheToggle;
            Toggle m_LiveLinkAssetCacheToggle;
            Toggle m_LiveLinkPlayerCacheToggle;

            public void Initialize(EditorWindow editorWindow, VisualElement rootVisualElement)
            {
                VisualTreeAsset uiAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.entities/Unity.Scenes.Editor/ClearEntitiesCacheWindow/ClearEntitiesCacheWindow.uxml");
                rootVisualElement.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>("Packages/com.unity.entities/Unity.Scenes.Editor/ClearEntitiesCacheWindow/ClearEntitiesCacheWindow.uss"));

                m_Root = uiAsset.CloneTree();
                m_Root.style.flexGrow = 1;

                m_CancelButton = m_Root.Query<Button>("cancel-button");
                m_CancelButton.clicked += () =>
                {
                    editorWindow.Close();
                };

                m_ClearButton = m_Root.Query<Button>("clear-cache-button");
                m_ClearButton.clicked += () =>
                {
                    if (m_EntitySceneCacheToggle.value)
                    {
                        EntitiesCacheUtility.UpdateEntitySceneGlobalDependency();
                    }

                    if (m_LiveLinkAssetCacheToggle.value)
                    {
                        EntitiesCacheUtility.UpdateLiveLinkAssetGlobalDependency();
                    }

                    if (m_LiveLinkPlayerCacheToggle.value)
                    {
                        LiveLinkUtility.GenerateNewEditorLiveLinkCacheGUID();
                    }

                    editorWindow.Close();
                };

                m_EntitySceneCacheToggle = m_Root.Query<Toggle>("clear-entityscene-toggle");
                m_LiveLinkAssetCacheToggle = m_Root.Query<Toggle>("clear-livelinkassets-toggle");
                m_LiveLinkPlayerCacheToggle = m_Root.Query<Toggle>("clear-livelinkplayer-toggle");


                rootVisualElement.Add(m_Root);
            }
        }
    }
}
