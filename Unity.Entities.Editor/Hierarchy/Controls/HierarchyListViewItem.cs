using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Editor.Bridge;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using HierarchyModel = Unity.Entities.Editor.Hierarchy;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// Implement this interface to inject custom behavior to the hierarchy list element(s).
    /// </summary>
    interface IHierarchyItemDecorator
    {
        /// <summary>
        /// Invoked once when the hierarchy element is constructed before any data is bound. Use this method to construct any common elements that should always be present.
        /// </summary>
        /// <param name="item">The hierarchy item visual element.</param>
        void OnCreateItem(HierarchyListViewItem item);

        /// <summary>
        /// Invoked when an element is bound to a new data source.
        /// </summary>
        /// <remarks>
        /// This method can be called multiple times with the same data source.
        /// </remarks>
        /// <param name="item">The hierarchy item visual element.</param>
        /// <param name="node">The node data.</param>
        void OnBindItem(HierarchyListViewItem item, HierarchyNode.Immutable node);

        /// <summary>
        /// Invoked when an element is unbound from an existing data source.
        /// </summary>
        /// <remarks>
        /// This is not called 1:1 with <see cref="OnBindItem"/>.
        /// </remarks>
        /// <param name="item">The hierarchy item visual element.</param>
        void OnUnbindItem(HierarchyListViewItem item);

        /// <summary>
        /// Invoked when an element is destroyed by the tree view.
        /// </summary>
        /// <param name="item">The hierarchy item visual element.</param>
        void OnDestroyItem(HierarchyListViewItem item);
    }

    class HierarchyDecoratorCollection : IEnumerable<IHierarchyItemDecorator>
    {
        readonly List<IHierarchyItemDecorator> m_Decorators = new List<IHierarchyItemDecorator>();

        public Action<IHierarchyItemDecorator> OnAdd;
        public Action<IHierarchyItemDecorator> OnRemove;

        public void Add(IHierarchyItemDecorator decorator)
        {
            if (m_Decorators.Contains(decorator))
                return;

            m_Decorators.Add(decorator);
            OnAdd?.Invoke(decorator);
        }

        public void Remove(IHierarchyItemDecorator decorator)
        {
            if (!m_Decorators.Contains(decorator))
                return;

            m_Decorators.Remove(decorator);
            OnRemove?.Invoke(decorator);
        }

        public List<IHierarchyItemDecorator>.Enumerator GetEnumerator()
            => m_Decorators.GetEnumerator();

        IEnumerator<IHierarchyItemDecorator> IEnumerable<IHierarchyItemDecorator>.GetEnumerator()
            => m_Decorators.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    /// <summary>
    /// The <see cref="HierarchyListViewItem"/> represents the visual elements for a single row in the hierarchy.
    /// </summary>
    class HierarchyListViewItem : VisualElement
    {
        const int k_TabWidth = 16;
        const int k_MaxDepth = 128;

        /// <summary>
        /// The interval between updates in milliseconds.
        /// </summary>
        const long k_UpdateRate = 150;

        static readonly string k_PingSubSceneInHierarchy = L10n.Tr("Ping sub scene in hierarchy");
        static readonly string k_PingSubSceneInProjectWindow = L10n.Tr("Ping sub scene in project window");

        const string k_UnityListViewItemName = "unity-list-view__item";
        const string k_UnityTreeViewItemName = "unity-tree-view__item";
        const string k_UnityTreeViewItemToggleName = "unity-tree-view__item-toggle";
        const string k_UnityTreeViewItemIndentsName = "unity-tree-view__item-indents";
        const string k_UnityTreeViewItemIndentName = "unity-tree-view__item-indent";
        const string k_UnityTreeViewItemModeIndentName = "unity-tree-view__item-mode-indent";
        const string k_UnityTreeViewItemContentName = "unity-tree-view__item-content";
        const string k_UnityListViewItemSelectedClassName = "unity-list-view__item--selected";

        readonly HierarchyModel m_Model;

        // Generic Hierarchy Elements
        readonly Toggle m_Toggle;
        readonly VisualElement m_IndentContainer;
        readonly VisualElement m_ContentContainer;
        readonly VisualElement m_Column1;
        readonly VisualElement m_Column2;

        readonly Label m_Name;
        readonly Label m_SubSceneState;
        readonly VisualElement m_Icon;
        readonly VisualElement m_SystemButton;
        readonly VisualElement m_PingGameObject;
        readonly VisualElement m_PrefabStageButton;

        readonly IVisualElementScheduledItem m_UpdateEventHandler;
        IManipulator m_ContextMenuManipulator;

        bool m_IsSelected;

        public override VisualElement contentContainer => m_Column1;

        /// <summary>
        /// The current index this element is bound to.
        /// </summary>
        public int Index { get; private set; } = -1;

        /// <summary>
        /// The current handle this element is bound to.
        /// </summary>
        public HierarchyNodeHandle Handle { get; private set; } = new HierarchyNodeHandle(NodeKind.None, index: -1, version: -1);

        /// <summary>
        /// Gets the <see cref="GameObject"/> this element is bound to, if any; null otherwise.
        /// </summary>
        public GameObject GameObject => Handle.Kind == NodeKind.GameObject ? Handle.ToGameObject() : null;

        /// <summary>
        /// Gets the <see cref="Entity"/> this element is bound to, if any; Entity.Null otherwise.
        /// </summary>
        public Entity Entity => Handle.Kind == NodeKind.Entity ? Handle.ToEntity() : Entity.Null;

        /// <summary>
        /// Returns the currently active world for the hierarchy.
        /// </summary>
        public World World => m_Model.World;

        /// <summary>
        /// Gets the <see cref="HierarchyModel.HierarchyPrefabType"/> for this element.
        /// </summary>
        public HierarchyModel.HierarchyPrefabType PrefabType => m_Model.GetPrefabType(Handle);

        /// <summary>
        /// Gets the <see cref="HierarchyModel.DataMode"/> for this element.
        /// </summary>
        public DataMode DataMode => m_Model.DataMode;

        /// <summary>
        /// The label representing the name for this element.
        /// </summary>
        public Label NameLabel => m_Name;

        /// <summary>
        /// The VisualElement representing the displayed Icon for this element.
        /// </summary>
        public VisualElement Icon => m_Icon;

        /// <summary>
        /// The VisualElement representing the first column (containing the dropdown, icon and label).
        /// </summary>
        public VisualElement Column1 => m_Column1;

        /// <summary>
        /// The VisualElement representing the second, right-hand column.
        /// </summary>
        public VisualElement Column2 => m_Column2;

        int m_ChangeVersion;
        bool m_Expanded;
        bool m_Filtered;
        bool m_DataModeChanged;

        NodeKind m_CurrentStyle;
        readonly VisualElement m_ModeIndent;

        public HierarchyListViewItem(HierarchyModel model)
        {
            m_Model = model;

            // Setup the root. This is taken from 'TreeView.MakeTreeItem'
            name = k_UnityTreeViewItemName;
            AddToClassList(k_UnityTreeViewItemName);
            AddToClassList(k_UnityListViewItemName);
            style.flexDirection = FlexDirection.Row;

            // Create tree view specific elements. Indent, foldout etc.
            m_Toggle = new Toggle {name = k_UnityTreeViewItemToggleName};
            m_IndentContainer = new VisualElement {name = k_UnityTreeViewItemIndentsName, style = {flexDirection = FlexDirection.Row}};
            m_ContentContainer = new VisualElement {name = k_UnityTreeViewItemContentName, style = {flexGrow = 1, flexShrink = 1}};
            m_ModeIndent = new VisualElement { name = k_UnityTreeViewItemModeIndentName };

            // Setup styling for tree item specific elements.
            m_Toggle.AddToClassList(Foldout.toggleUssClassName);
            m_IndentContainer.AddToClassList(k_UnityTreeViewItemIndentsName);
            m_ModeIndent.AddToClassList(k_UnityTreeViewItemModeIndentName);

            Resources.Templates.Hierarchy.Item.Clone(m_ContentContainer);
            m_ContentContainer.AddToClassList(UssClasses.DotsEditorCommon.CommonResources);
            m_ContentContainer.AddToClassList(k_UnityTreeViewItemContentName);
            m_Column1 = m_ContentContainer.Q<VisualElement>(name: "Column 1");
            m_Column2 = m_ContentContainer.Q<VisualElement>(name: "Column 2");

            m_Name = m_ContentContainer.Q<Label>(className: UssClasses.Hierarchy.Item.NameLabel);
            m_SubSceneState = m_ContentContainer.Q<Label>(className: UssClasses.Hierarchy.Item.SubSceneState);
            m_Icon = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.Icon);
            m_SystemButton = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.SystemButton);
            m_PingGameObject = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.PingGameObjectButton);
            m_PrefabStageButton = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.PrefabStageButton);

            var prefabStageClickable = new Clickable(OnOpenPrefab);
            prefabStageClickable.activators.Add(new ManipulatorActivationFilter {button = MouseButton.LeftMouse, modifiers = EventModifiers.Alt});
            m_PrefabStageButton.AddManipulator(prefabStageClickable);

            hierarchy.Add(m_ModeIndent);
            hierarchy.Add(m_IndentContainer);
            hierarchy.Add(m_Toggle);
            hierarchy.Add(m_ContentContainer);

            m_UpdateEventHandler = schedule.Execute(OnUpdate).Every(k_UpdateRate);
            m_UpdateEventHandler.Pause();
        }

        public void RegisterEventListeners()
        {
            m_Model.DataModeChanged += OnDataModeChanged;
        }

        public void UnregisterEventListeners()
        {
            m_Model.DataModeChanged -= OnDataModeChanged;
        }

        void OnDataModeChanged()
        {
            m_DataModeChanged = true;
        }

        public bool IsChanged(HierarchyNode.Immutable node)
        {
            if (m_DataModeChanged)
                return true;

            if (Handle != node.GetHandle())
                return true;

            if (m_ChangeVersion != node.GetChangeVersion())
                return true;

            if (m_Expanded != m_Model.IsExpanded(node.GetHandle()))
                return true;

            if (m_Filtered != m_Model.HasSearchFilter())
                return true;

            return false;
        }

        public void Bind(int index, HierarchyNode.Immutable node)
        {
            if (Index != -1)
            {
                // @FIXME For some reason we do not receive proper 1:1 bind/unbind calls when using multi-column list views.
                //        This guards against rebinding an element without properly clearing previous styling.
                Unbind();
            }

            m_UpdateEventHandler.Pause();

            Index = index;
            Handle = node.GetHandle();
            m_ChangeVersion = node.GetChangeVersion();
            m_Expanded = m_Model.IsExpanded(Handle);
            m_Filtered = m_Model.HasSearchFilter();

            var showExpandCollapseToggle = !m_Filtered && node.GetChildCount() > 0;

            m_Name.text = m_Model.GetName(node.GetHandle());
            m_Toggle.visible = showExpandCollapseToggle;
            m_Toggle.SetValueWithoutNotify(showExpandCollapseToggle && m_Expanded);
            m_PrefabStageButton.SetVisibility(false);

            var depth = node.GetDepth();

            if (depth >= k_MaxDepth)
                throw new InvalidOperationException($"Node depth is greater than {k_MaxDepth}");

            if (!m_Filtered)
                m_IndentContainer.style.width = k_TabWidth * depth;
            else
                m_IndentContainer.style.width = 0;

            m_CurrentStyle = node.GetHandle().Kind;

            // Update the style for the node. This will add/remove classes and handle registering callbacks.
            switch (m_CurrentStyle)
            {
                case NodeKind.Entity:
                    AddEntityStyle(node);
                    break;
                case NodeKind.GameObject:
                    AddGameObjectStyle(node);
                    break;
                case NodeKind.Scene:
                    AddSceneStyle(node);
                    break;
                case NodeKind.SubScene:
                    AddSubSceneStyle(node);
                    break;
            }

            Refresh();
            m_UpdateEventHandler.Resume();
        }

        public void Unbind()
        {
            Index = -1;
            Handle = default;

            Reset();
        }

        /// <summary>
        /// Clears the any binding data for this visual element.
        /// </summary>
        void Reset()
        {
            m_UpdateEventHandler.Pause();
            m_ModeIndent.RemoveFromClassList(UssClasses.Hierarchy.Item.RuntimeModeIndent);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.Prefab);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.PrefabRoot);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_Name.RemoveFromClassList(UssClasses.Hierarchy.Item.NameScene);
            m_SubSceneState.text = string.Empty;
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconScene);
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconEntity);
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconGameObject);
            m_SystemButton.RemoveFromClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
            m_PingGameObject.RemoveFromClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
            m_PingGameObject.UnregisterCallback<MouseUpEvent>(OnPingGameObject);
            m_PrefabStageButton.SetVisibility(false);
            RemoveFromClassList(UssClasses.Hierarchy.Item.SubSceneNode);

            if (null != m_ContextMenuManipulator)
            {
                this.RemoveManipulator(m_ContextMenuManipulator);
                m_ContextMenuManipulator = null;
            }

            switch (m_CurrentStyle)
            {
                case NodeKind.Entity:
                    RemoveEntityStyle();
                    break;
                case NodeKind.GameObject:
                    RemoveGameObjectStyle();
                    break;
                case NodeKind.Scene:
                    RemoveSceneStyle();
                    break;
                case NodeKind.SubScene:
                    RemoveSubSceneStyle();
                    break;
            }

            m_CurrentStyle = NodeKind.None;
            m_IndentContainer.Clear();
        }

        void AddEntityStyle(HierarchyNode.Immutable node)
        {
            m_Model.DataModeChanged += UpdateModeIndentColor;
            UpdateModeIndentColor();
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconEntity);

            if (m_Model.GetUnityObject(node.GetHandle()))
            {
                m_PingGameObject.AddToClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
                m_PingGameObject.RegisterCallback<MouseUpEvent>(OnPingGameObject);
            }
        }

        void RemoveEntityStyle()
        {
            m_Model.DataModeChanged -= UpdateModeIndentColor;
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconEntity);
            m_PingGameObject.RemoveFromClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
            m_PingGameObject.UnregisterCallback<MouseUpEvent>(OnPingGameObject);
        }

        void AddGameObjectStyle(HierarchyNode.Immutable node)
        {
            m_Model.DataModeChanged += UpdateModeIndentColor;
            UpdateModeIndentColor();

            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconGameObject);

            var unityObject = m_Model.GetUnityObject(node.GetHandle());

            if (unityObject)
            {
                m_PingGameObject.AddToClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
                m_PingGameObject.RegisterCallback<MouseUpEvent>(OnPingGameObject);

                var isPrefabStageButtonVisible = PrefabUtility.IsAnyPrefabInstanceRoot(unityObject as GameObject);
                var stage = PrefabStageUtility.GetCurrentPrefabStage();

                if (isPrefabStageButtonVisible && stage)
                    isPrefabStageButtonVisible = stage.prefabContentsRoot != unityObject as GameObject;

                m_PrefabStageButton.SetVisibility(isPrefabStageButtonVisible);
            }
        }

        void RemoveGameObjectStyle()
        {
            m_Model.DataModeChanged -= UpdateModeIndentColor;
        }

        void UpdateModeIndentColor()
        {
            switch (Handle.Kind)
            {
                case NodeKind.GameObject when m_Model.DataMode is DataMode.Mixed:
                {
                    var unityObject = m_Model.GetUnityObject(Handle) as GameObject;
                    m_ModeIndent.EnableInClassList(UssClasses.Hierarchy.Item.RuntimeModeIndent, unityObject && !unityObject.scene.isSubScene);
                    break;
                }
                case NodeKind.SubScene or NodeKind.DynamicSubScene:
                case NodeKind.GameObject:
                case NodeKind.Entity:
                    m_ModeIndent.EnableInClassList(UssClasses.Hierarchy.Item.RuntimeModeIndent, m_Model.DataMode is DataMode.Mixed or DataMode.Runtime);
                    break;
            }
        }

        void AddSceneStyle(HierarchyNode.Immutable node)
        {
            m_Name.AddToClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.AddToClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_SubSceneState.text = string.Empty;
        }

        void RemoveSceneStyle()
        {
            m_Name.RemoveFromClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_SubSceneState.text = string.Empty;
        }

        void AddSubSceneStyle(HierarchyNode.Immutable node)
        {
            m_Model.DataModeChanged += UpdateModeIndentColor;
            UpdateModeIndentColor();
            AddToClassList(UssClasses.Hierarchy.Item.SubSceneNode);
            m_Name.AddToClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.AddToClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_SubSceneState.text = string.Empty;
            m_ContextMenuManipulator = new ContextualMenuManipulator(null);
            this.AddManipulator(m_ContextMenuManipulator);
            RegisterCallback<ContextualMenuPopulateEvent>(OnSubSceneContextMenu);
        }

        void RemoveSubSceneStyle()
        {
            m_Model.DataModeChanged -= UpdateModeIndentColor;
            RemoveFromClassList(UssClasses.Hierarchy.Item.SubSceneNode);
            m_Name.RemoveFromClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_SubSceneState.text = string.Empty;

            if (null != m_ContextMenuManipulator)
            {
                this.RemoveManipulator(m_ContextMenuManipulator);
                m_ContextMenuManipulator = null;
            }

            UnregisterCallback<ContextualMenuPopulateEvent>(OnSubSceneContextMenu);
        }

        void OnUpdate()
        {
            Refresh();
        }

        void Refresh()
        {
            UpdateName();

            if (Handle.Kind == NodeKind.SubScene)
            {
                if (m_Model.World == null || !m_Model.World.IsCreated)
                    return;

                var state = m_Model.GetSubSceneState(Handle);
                m_SubSceneState.text = state switch
                {
                    HierarchyModel.SubSceneLoadedState.Closed => L10n.Tr("(closed)"),
                    HierarchyModel.SubSceneLoadedState.NotLoaded => L10n.Tr("(not loaded)"),
                    HierarchyModel.SubSceneLoadedState.LiveConverted => L10n.Tr("(Live converted)"),
                    HierarchyModel.SubSceneLoadedState.Opened => L10n.Tr("(opened)"),
                    _ => string.Empty
                };

                style.opacity = state == HierarchyModel.SubSceneLoadedState.NotLoaded ? 0.5f : 1f;
            }
            else if (Handle.Kind == NodeKind.Scene)
            {
                var scene = EditorSceneManagerBridge.GetSceneByHandle(Handle.Index);

                m_SubSceneState.text = scene.isLoaded ? string.Empty : L10n.Tr("(not loaded)");
                style.opacity = scene.isLoaded ? 1f : 0.5f;
            }
            else
            {
                style.opacity = m_Model.IsDisabled(Handle) ? 0.5f : 1f;

                var prefab = m_Model.GetPrefabType(Handle);

                switch (prefab)
                {
                    case HierarchyModel.HierarchyPrefabType.None:
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.Prefab, false);
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.PrefabRoot, false);
                        break;
                    case HierarchyModel.HierarchyPrefabType.PrefabRoot:
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.Prefab, true);
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.PrefabRoot, true);
                        break;
                    case HierarchyModel.HierarchyPrefabType.PrefabPart:
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.Prefab, true);
                        m_ContentContainer.EnableInClassList(UssClasses.Hierarchy.Item.PrefabRoot, false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        void UpdateName()
        {
            var str = default(FixedString64Bytes);
            m_Model.GetName(Handle, ref str);

            if (!str.Equals(m_Name.text))
                m_Name.text = str.ToString();
        }

        void OnSubSceneContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction(k_PingSubSceneInHierarchy, OnPingSubSceneInHierarchy);
            evt.menu.AppendAction(k_PingSubSceneInProjectWindow, OnPingSubSceneAsset);
        }

        void OnPingGameObject(MouseUpEvent _)
        {
            EditorGUIUtility.PingObject(m_Model.GetInstanceId(Handle));
        }

        void OnPingSubSceneInHierarchy(DropdownMenuAction obj)
        {
            EditorGUIUtility.PingObject(m_Model.GetInstanceId(Handle));
        }

        void OnPingSubSceneAsset(DropdownMenuAction obj)
        {
            if (m_Model.GetUnityObject(Handle) is SubScene subScene)
            {
                EditorGUIUtility.PingObject(subScene.SceneAsset);
            }
        }

        void OnOpenPrefab(EventBase evt)
        {
            var prefabInstance = EditorUtility.InstanceIDToObject(m_Model.GetInstanceId(Handle)) as GameObject;
            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefabInstance);
            var defaultPrefabMode = PreferencesProviderBridge.GetDefaultPrefabModeForHierarchy();
            var alternativePrefabMode = (defaultPrefabMode == PrefabStage.Mode.InContext) ? PrefabStage.Mode.InIsolation : PrefabStage.Mode.InContext;
            var mode = (evt as MouseUpEvent).modifiers.HasFlag(EventModifiers.Alt) ? alternativePrefabMode : defaultPrefabMode;
            PrefabStageUtility.OpenPrefab(assetPath, prefabInstance, mode);
        }
    }
}
