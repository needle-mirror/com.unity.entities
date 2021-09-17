using Unity.Collections;
using Unity.Profiling;
using Unity.Properties.UI;
using Unity.Scenes;
using Unity.Serialization.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    partial class HierarchyWindow : DOTSEditorWindow
    {
        static readonly ProfilerMarker k_OnUpdateMarker = new ProfilerMarker($"{nameof(HierarchyWindow)}.{nameof(OnUpdate)}");

        static readonly string k_FilterComponentType = L10n.Tr("Component type");
        static readonly string k_FilterComponentTypeTooltip = L10n.Tr("Filter entities that have the specified component type");

        static readonly string k_WindowName = L10n.Tr("DOTS Hierarchy");
        static readonly Vector2 k_MinWindowSize = new Vector2(200, 200); // Matches SceneHierarchy's min size

        Hierarchy m_Hierarchy;
        HierarchyElement m_HierarchyElement;

        VisualElement m_Toolbar;
        VisualElement m_NoWorldMessage;
        VisualElement m_EnableLiveConversionMessage;

        bool m_ContainsAnyWorld;

        /// <summary>
        /// Indicates if the window is visible or not. The window can be 'open' but docked as a tab.
        /// </summary>
        bool m_IsVisible;

        /// <summary>
        /// Flag indicating we should try to apply the global selection on the next update cycle.
        /// </summary>
        bool m_GlobalSelectionRequest;

        /// <summary>
        /// The update version this request was made at.
        /// </summary>
        uint m_GlobalSelectionRequestUpdateVersion;

        [MenuItem(Constants.MenuItems.HierarchyWindow, false, Constants.MenuItems.WindowPriority)]
        static void OpenWindow() => GetWindow<HierarchyWindow>();

        void OnEnable()
        {
            titleContent = new GUIContent(k_WindowName, EditorIcons.EntityGroup);
            minSize = k_MinWindowSize;

            Resources.Templates.DotsEditorCommon.AddStyles(rootVisualElement);
            Resources.AddCommonVariables(rootVisualElement);

            // Initialize the data models
            m_Hierarchy = new Hierarchy(Allocator.Persistent)
            {
                Configuration = UserSettings<HierarchySettings>.GetOrCreate(Constants.Settings.Hierarchy).Configuration,
                State = SessionState<HierarchyState>.GetOrCreate($"{GetType().Name}.{nameof(HierarchyState)}")
            };

            // Initialize the view models.
            m_Toolbar = CreateToolbar(rootVisualElement, m_Hierarchy);
            m_HierarchyElement = CreateHierarchyElement(rootVisualElement, m_Hierarchy);
            m_NoWorldMessage = CreateNoWorldMessage(rootVisualElement);
            m_EnableLiveConversionMessage = CreateEnableLiveConversionMessage(rootVisualElement);

            Selection.selectionChanged += OnGlobalSelectionChanged;
            LiveConversionConfigHelper.LiveConversionEnabledChanged += OnLiveConversionEnabledChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            World.WorldDestroyed += OnWorldDestroyed;

            UpdateVisibility();
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnGlobalSelectionChanged;
            LiveConversionConfigHelper.LiveConversionEnabledChanged -= OnLiveConversionEnabledChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            World.WorldDestroyed -= OnWorldDestroyed;
            m_Hierarchy.Dispose();
        }

        void OnBecameVisible()
        {
            m_IsVisible = true;

            // Request the selection to be updated after the next update cycle.
            m_GlobalSelectionRequest = true;
            m_GlobalSelectionRequestUpdateVersion = m_Hierarchy?.UpdateVersion ?? 0;
        }

        void OnBecameInvisible()
        {
            m_IsVisible = false;
        }

        HierarchyElement CreateHierarchyElement(VisualElement root, Hierarchy hierarchy)
        {
            var element = new HierarchyElement(hierarchy);
            element.OnSelectionChanged += OnHierarchySelectionChanged;
            root.Add(element);
            return element;
        }

        VisualElement CreateToolbar(VisualElement root, Hierarchy hierarchy)
        {
            var toolbar = Resources.Templates.Hierarchy.Toolbar.Clone();
            var leftSide = toolbar.Q<VisualElement>(className: UssClasses.Hierarchy.Toolbar.LeftSide);
            var rightSide = toolbar.Q<VisualElement>(className: UssClasses.Hierarchy.Toolbar.RightSide);
            leftSide.Add(CreateWorldSelector());

            AddSearchIcon(rightSide, UssClasses.DotsEditorCommon.SearchIcon);
            var searchElement = AddSearchElement<HierarchyNodeHandle>(toolbar, UssClasses.DotsEditorCommon.SearchFieldContainer);
            searchElement.RegisterSearchQueryHandler<HierarchyNodeHandle>(query =>
            {
                hierarchy.SetSearchQuery(query.SearchString, query.Tokens);
            });

            searchElement.AddSearchFilterPopupItem(Constants.EntityHierarchy.ComponentToken, k_FilterComponentType, k_FilterComponentTypeTooltip);
            searchElement.EnableAutoComplete(ComponentTypeAutoComplete.Instance);
            root.Add(toolbar);
            return toolbar;
        }

        VisualElement CreateEnableLiveConversionMessage(VisualElement root)
        {
            var element = new VisualElement { style = { flexGrow = 1 } };
            Resources.Templates.EntityHierarchyEnableLiveConversionMessage.Clone(element);
            element.Q<Button>().clicked += () => LiveConversionConfigHelper.LiveConversionEnabledInEditMode = true;
            root.Add(element);
            return element;
        }

        VisualElement CreateNoWorldMessage(VisualElement root)
        {
            var message = new CenteredMessageElement { Message = NoWorldMessageContent };
            root.Add(message);
            message.Hide();
            return message;
        }

        void OnGlobalSelectionChanged()
        {
            if (!m_IsVisible)
                return;

            if (!(Selection.activeContext is HierarchySelectionContext))
            {
                SetSelection(Selection.activeObject);
            }
        }

        void SetSelection(Object obj)
        {
            if (obj is EntitySelectionProxy selectedProxy && selectedProxy.World == m_Hierarchy.World)
            {
                m_HierarchyElement.SetSelection(HierarchyNodeHandle.FromEntity(selectedProxy.Entity));
            }
            else if (obj is GameObject gameObject && gameObject.GetComponent<SubScene>() != null)
            {
                if (null != m_Hierarchy.World)
                    m_HierarchyElement.SetSelection(HierarchyNodeHandle.FromSubScene(m_Hierarchy.World, gameObject.GetComponent<SubScene>()));
            }
            else
            {
                m_HierarchyElement.ClearSelection();
            }
        }

        void OnHierarchySelectionChanged(HierarchyNodeHandle handle)
        {
            if (!m_IsVisible)
                return;

            switch (handle.Kind)
            {
                case NodeKind.Entity:
                {
                    var entity = handle.ToEntity();

                    if (entity != Entity.Null)
                    {
                        var undoGroup = Undo.GetCurrentGroup();
                        EntitySelectionProxy.SelectEntity(m_Hierarchy.World, entity);
                        Undo.CollapseUndoOperations(undoGroup);
                    }

                    break;
                }

                case NodeKind.SubScene:
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    var subScene = m_Hierarchy.GetUnityObject(handle) as SubScene;
                    Selection.SetActiveObjectWithContext(subScene ? subScene.gameObject : null, HierarchySelectionContext.CreateInstance(handle));
                    Undo.CollapseUndoOperations(undoGroup);
                    break;
                }

                case NodeKind.Scene:
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    Selection.SetActiveObjectWithContext(null, HierarchySelectionContext.CreateInstance(handle));
                    Undo.CollapseUndoOperations(undoGroup);
                    break;
                }
            }
        }

        void OnPlayModeStateChanged(PlayModeStateChange playModeStateChange)
        {
            UpdateVisibility();
        }

        void OnLiveConversionEnabledChanged()
        {
            UpdateVisibility();
        }

        void OnWorldDestroyed(World world)
        {
            if (SelectedWorld == world)
                OnWorldSelected(null);
        }

        protected override void OnWorldsChanged(bool containsAnyWorld)
        {
            m_ContainsAnyWorld = containsAnyWorld;

            // @FIXME This callback is invoked when adding the toolbar BEFORE we are done building the UI.
            if (null != m_NoWorldMessage)
                UpdateVisibility();
        }

        void UpdateVisibility()
        {
            m_NoWorldMessage.SetVisibility(!m_ContainsAnyWorld);
            m_EnableLiveConversionMessage.SetVisibility(false);
            m_HierarchyElement.SetVisibility(m_ContainsAnyWorld);
            m_Toolbar.SetVisibility(m_ContainsAnyWorld);
        }

        protected override void OnWorldSelected(World world)
        {
            m_Hierarchy.SetWorld(world);
            m_HierarchyElement.Reset();

            // Force the search element to update if we have something serialized.
            rootVisualElement.Q<SearchElement>().Search(SearchFilter);
        }

        protected override void OnUpdate()
        {
            using (k_OnUpdateMarker.Auto())
            {
                m_Hierarchy.Update();
                m_HierarchyElement.Refresh();

                // We have a deferred selection request. Wait until we have completed a full update cycle before attempting to select.
                if (m_GlobalSelectionRequest && m_Hierarchy.UpdateVersion == m_GlobalSelectionRequestUpdateVersion + 1)
                {
                    SetSelection(Selection.activeObject);
                    m_GlobalSelectionRequest = false;
                }
            }
        }
    }
}
