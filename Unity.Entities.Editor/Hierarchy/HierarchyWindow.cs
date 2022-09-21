using System;
using Unity.Collections;
using Unity.Editor.Bridge;
using Unity.Profiling;
using Unity.Platforms.UI;
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

        static readonly string k_WindowName = L10n.Tr("Entities Hierarchy");
        static readonly Vector2 k_MinWindowSize = Constants.MinWindowSize;

        static readonly HierarchyDecoratorCollection s_Decorators = new HierarchyDecoratorCollection();

        internal static void AddDecorator(IHierarchyItemDecorator decorator) => s_Decorators.Add(decorator);
        internal static void RemoveDecorator(IHierarchyItemDecorator decorator) => s_Decorators.Remove(decorator);

        Hierarchy m_Hierarchy;
        HierarchyElement m_HierarchyElement;

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
        SearchElement m_SearchElement;

        [MenuItem(Constants.MenuItems.HierarchyWindow, false, Constants.MenuItems.HierarchyWindowPriority)]
        static void OpenWindow() => GetWindow<HierarchyWindow>();

        public HierarchyWindow() : base(Analytics.Window.Hierarchy)
        { }

        void OnEnable()
        {
            titleContent = new GUIContent(k_WindowName, EditorIcons.EntityGroup);
            minSize = k_MinWindowSize;

            Resources.Templates.DotsEditorCommon.AddStyles(rootVisualElement);
            Resources.AddCommonVariables(rootVisualElement);

            m_DataModeHandler.dataModeChanged += OnCurrentDataModeChanged;

            // Initialize the data models
            m_Hierarchy = new Hierarchy(Allocator.Persistent, m_DataModeHandler.dataMode)
            {
                Configuration = UserSettings<HierarchySettings>.GetOrCreate(Constants.Settings.Hierarchy).Configuration,
                State = SessionState<HierarchyState>.GetOrCreate($"{GetType().Name}.{nameof(HierarchyState)}")
            };

            // Initialize the view models.
            CreateToolbar(rootVisualElement, m_Hierarchy);
            m_HierarchyElement = CreateHierarchyElement(rootVisualElement, m_Hierarchy);
            m_HierarchyElement.SetDecorators(s_Decorators);

            Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        void OnDisable()
        {
            Selection.selectionChanged -= OnGlobalSelectionChanged;
            m_Hierarchy.Dispose();
        }

        void OnBecameVisible()
        {
            m_IsVisible = true;
            RequestGlobalSelectionRestoration();
        }

        void RequestGlobalSelectionRestoration()
        {
            // Request the selection to be updated after the next update cycle.
            m_GlobalSelectionRequest = true;
            m_GlobalSelectionRequestUpdateVersion = m_Hierarchy?.UpdateVersion ?? 0;
        }

        void OnBecameInvisible()
        {
            m_IsVisible = false;
        }

        void OnCurrentDataModeChanged(DataMode mode)
        {
            RequestGlobalSelectionRestoration();
            m_Hierarchy.SetDataMode(mode);
            Analytics.SendEditorEvent(Analytics.Window.Hierarchy, Analytics.EventType.DataModeSwitch, mode.ToString());
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
            m_SearchElement = AddSearchElement<HierarchyNodeHandle>(toolbar, UssClasses.DotsEditorCommon.SearchFieldContainer);
            m_SearchElement.RegisterSearchQueryHandler<HierarchyNodeHandle>(query =>
            {
                hierarchy.SetSearchQuery(query.SearchString, query.Tokens);
            });

            m_SearchElement.AddSearchFilterPopupItem(Constants.Hierarchy.ComponentToken, k_FilterComponentType, k_FilterComponentTypeTooltip);
            m_SearchElement.EnableAutoComplete(ComponentTypeAutoComplete.Instance);

            root.Add(toolbar);

            return toolbar;
        }

        void OnGlobalSelectionChanged() => ApplyGlobalSelection(false);

        void ApplyGlobalSelection(bool allowReentry)
        {
            if (!m_IsVisible)
                return;

            // If this is a re-entry, bail out
            if (Selection.activeContext is HierarchySelectionContext && !allowReentry)
                return;

            switch (m_DataModeHandler.dataMode)
            {
                case DataMode.Authoring:
                case DataMode.Mixed:
                {
                    // In Authoring Mode or Mixed Mode, we are always interacting
                    // with the active selection (Hybrid Object, GameObject, or Entity)
                    SetSelection(Selection.activeObject);
                    return;
                }
                case DataMode.Runtime:
                {
                    // In runtime, try real hard to show an Entity if available
                    if (Selection.activeContext is EntitySelectionProxy proxy && proxy.Exists)
                        SetSelection(proxy);
                    else
                        SetSelection(Selection.activeObject);
                    return;
                }
                case DataMode.Disabled:
                default:
                {
                    return;
                }
            }
        }

        void SetSelection(UnityEngine.Object obj)
        {
            if (obj is EntitySelectionProxy selectedProxy && selectedProxy.World == m_Hierarchy.World)
            {
                m_HierarchyElement.SetSelection(HierarchyNodeHandle.FromEntity(selectedProxy.Entity));
            }
            else if (obj is GameObject gameObject)
            {
                if (m_Hierarchy.World != null && gameObject.TryGetComponent<SubScene>(out var subScene))
                    m_HierarchyElement.SetSelection(m_Hierarchy.GetSubSceneNodeHandle(subScene));
                else
                    m_HierarchyElement.SetSelection(HierarchyNodeHandle.FromGameObject(gameObject));
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

                        var world = m_Hierarchy.World;
                        var authoringObject = world.EntityManager.Debug.GetAuthoringObjectForEntity(entity);

                        if (authoringObject == null)
                        {
                            EntitySelectionProxy.SelectEntity(world, entity);
                        }
                        else
                        {
                            // Don't reselect yourself
                            if (Selection.activeObject == authoringObject)
                                return;

                            var context = EntitySelectionProxy.CreateInstance(world, entity);
                            // Selected entities should always try to show up in Runtime mode
                            SelectionBridge.SetSelection(authoringObject, context, DataMode.Runtime);
                        }

                        Undo.CollapseUndoOperations(undoGroup);
                    }

                    break;
                }

                case NodeKind.SubScene:
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    var subScene = m_Hierarchy.GetUnityObject(handle) as SubScene;
                    SelectionBridge.SetSelection(subScene ? subScene.gameObject : null, HierarchySelectionContext.CreateInstance(handle), DataMode.Disabled);
                    Undo.CollapseUndoOperations(undoGroup);
                    break;
                }

                case NodeKind.Scene:
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    SelectionBridge.SetSelection(null, HierarchySelectionContext.CreateInstance(handle), DataMode.Disabled);
                    Undo.CollapseUndoOperations(undoGroup);
                    break;
                }

                case NodeKind.GameObject:
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    var gameObject = m_Hierarchy.GetUnityObject(handle) as GameObject;

                    // Don't reselect yourself
                    if (Selection.activeObject == gameObject)
                        return;

                    var world = m_Hierarchy.World;
                    EntitySelectionProxy context;

                    if (world == null || !world.IsCreated)
                        context = null;
                    else
                    {
                        var primaryEntity = world.EntityManager.Debug.GetPrimaryEntityForAuthoringObject(gameObject);

                        context = primaryEntity != Entity.Null && world.EntityManager.SafeExists(primaryEntity)
                                    ? EntitySelectionProxy.CreateInstance(world, primaryEntity)
                                    : null;
                    }

                    // Selected GameObjects should use whatever the current DataMode for the hierarchy is
                    SelectionBridge.SetSelection(gameObject, context, m_DataModeHandler.dataMode);

                    Undo.CollapseUndoOperations(undoGroup);
                    break;
                }
            }
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
                    ApplyGlobalSelection(true);
                    m_GlobalSelectionRequest = false;
                }
            }
        }

        void OnGUI()
        {
            var evt = Event.current;

            if (evt.type is not (EventType.ExecuteCommand or EventType.ValidateCommand))
                return;

            var execute = evt.type is EventType.ExecuteCommand;

            switch (evt.commandName)
            {
                case EventCommandNamesBridge.Find:
                    evt.Use();
                    if (execute)
                        m_SearchElement?.Q<TextField>().Focus();
                    break;
                case EventCommandNamesBridge.Delete:
                case EventCommandNamesBridge.SoftDelete:
                case EventCommandNamesBridge.Duplicate:
                case EventCommandNamesBridge.Rename:
                case EventCommandNamesBridge.Cut:
                case EventCommandNamesBridge.Copy:
                case EventCommandNamesBridge.Paste:
                case EventCommandNamesBridge.SelectAll:
                case EventCommandNamesBridge.DeselectAll:
                case EventCommandNamesBridge.InvertSelection:
                case EventCommandNamesBridge.SelectChildren:
                case EventCommandNamesBridge.SelectPrefabRoot:
                    evt.Use();
                    break;
            }

            if (execute)
                m_HierarchyElement?.HandleCommand(evt.commandName);
        }

    }
}
