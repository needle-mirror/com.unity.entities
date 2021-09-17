using System;
using Unity.Scenes;
using UnityEditor;
using UnityEngine.UIElements;
using HierarchyModel = Unity.Entities.Editor.Hierarchy;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// The <see cref="HierarchyListViewItem"/> represents the visual elements for a single row in the hierarchy.
    /// </summary>
    class HierarchyListViewItem : Unity.Editor.Bridge.VisualElementBridge
    {
        /// <summary>
        /// Indent visual element pool. Each indent is handled as a unique visual element to allow for style re-loading.
        /// </summary>
        static readonly BasicPool<VisualElement> k_IndentPool = new BasicPool<VisualElement>(() => {
            var indentElement = new VisualElement();
            indentElement.AddToClassList(k_UnityTreeViewItemIndentName);
            return indentElement;
        });

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
        const string k_UnityTreeViewItemContentName = "unity-tree-view__item-content";
        const string k_UnityListViewItemSelectedClassName = "unity-list-view__item--selected";

        readonly HierarchyModel m_Model;

        // Generic Hierarchy Elements
        readonly Toggle m_Toggle;
        readonly VisualElement m_IndentContainer;
        readonly VisualElement m_ContentContainer;

        readonly Label m_Name;
        readonly Label m_SubSceneState;
        readonly VisualElement m_Icon;
        readonly VisualElement m_SystemButton;
        readonly VisualElement m_PingGameObject;

        readonly IVisualElementScheduledItem m_UpdateEventHandler;
        IManipulator m_ContextMenuManipulator;

        bool m_IsSelected;

        public override VisualElement contentContainer => m_ContentContainer;

        /// <summary>
        /// The current index this element is bound to.
        /// </summary>
        public int Index { get; private set; } = -1;

        /// <summary>
        /// The current handle this element is bound to.
        /// </summary>
        public HierarchyNodeHandle Handle { get; private set; } = new HierarchyNodeHandle(NodeKind.None, -1, -1);

        int m_ChangeVersion;
        int m_Depth;
        bool m_Expanded;
        bool m_Filtered;

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

            // Setup styling for tree item specific elements.
            m_Toggle.AddToClassList(Foldout.toggleUssClassName);
            m_IndentContainer.AddToClassList(k_UnityTreeViewItemIndentsName);

            Resources.Templates.Hierarchy.Item.Clone(m_ContentContainer);
            m_ContentContainer.AddToClassList(UssClasses.DotsEditorCommon.CommonResources);
            m_ContentContainer.AddToClassList(k_UnityTreeViewItemContentName);
            m_Name = m_ContentContainer.Q<Label>(className: UssClasses.Hierarchy.Item.NameLabel);
            m_SubSceneState = m_ContentContainer.Q<Label>(className: UssClasses.Hierarchy.Item.SubSceneState);
            m_Icon = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.Icon);
            m_SystemButton = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.SystemButton);
            m_PingGameObject = m_ContentContainer.Q<VisualElement>(className: UssClasses.Hierarchy.Item.PingGameObjectButton);

            hierarchy.Add(m_IndentContainer);
            hierarchy.Add(m_Toggle);
            hierarchy.Add(m_ContentContainer);

            m_UpdateEventHandler = schedule.Execute(OnUpdate).Every(k_UpdateRate);
            m_UpdateEventHandler.Pause();
        }

        public void SetSelected(bool selected)
        {
            if (!m_IsSelected && selected)
            {
                AddToClassList(k_UnityListViewItemSelectedClassName);
                pseudoStates |= 8; // PseudoStates.Checked
            }
            else if (m_IsSelected && !selected)
            {
                RemoveFromClassList(k_UnityListViewItemSelectedClassName);
                pseudoStates &= ~8; // PseudoStates.Checked
            }

            m_IsSelected = selected;
        }

        public bool IsChanged(HierarchyNode.Immutable node)
        {
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
            Reset();

            Index = index;
            Handle = node.GetHandle();
            m_ChangeVersion = node.GetChangeVersion();
            m_Expanded = m_Model.IsExpanded(Handle);
            m_Filtered = m_Model.HasSearchFilter();

            m_Name.text = m_Model.GetName(node.GetHandle());
            m_Toggle.visible = !m_Filtered && node.GetChildCount() > 0;
            m_Toggle.SetValueWithoutNotify(!m_Filtered && m_Expanded);

            var depth = node.GetDepth();

            if (depth >= k_MaxDepth)
                throw new InvalidOperationException($"Node depth is greater than {k_MaxDepth}");

            if (!m_Filtered)
            {
                for (var i = 0; i < node.GetDepth(); ++i)
                {
                    m_IndentContainer.Add(k_IndentPool.Acquire());
                }
            }

            switch (node.GetHandle().Kind)
            {
                case NodeKind.Entity:
                    AddEntityStyle(node);
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

            for (var i = 0; i < m_IndentContainer.childCount; i++)
                k_IndentPool.Release(m_IndentContainer[i]);

            m_IndentContainer.Clear();
        }

        /// <summary>
        /// Clears the any binding data for this visual element.
        /// </summary>
        void Reset()
        {
            m_UpdateEventHandler.Pause();
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.Prefab);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.PrefabRoot);
            m_ContentContainer.RemoveFromClassList(UssClasses.Hierarchy.Item.SceneNode);
            m_Name.RemoveFromClassList(UssClasses.Hierarchy.Item.NameScene);
            m_SubSceneState.text = string.Empty;
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconScene);
            m_Icon.RemoveFromClassList(UssClasses.Hierarchy.Item.IconEntity);
            m_SystemButton.RemoveFromClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
            m_PingGameObject.RemoveFromClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
            m_PingGameObject.UnregisterCallback<MouseUpEvent>(OnPingGameObject);
            RemoveFromClassList(UssClasses.Hierarchy.Item.SubSceneNode);

            if (m_ContextMenuManipulator != null)
            {
                this.RemoveManipulator(m_ContextMenuManipulator);
                UnregisterCallback<ContextualMenuPopulateEvent>(OnSubSceneContextMenu);
                m_ContextMenuManipulator = null;
            }
        }

        public void Attach()
        {
            AddToClassList(k_UnityListViewItemName);
            AddToClassList(k_UnityTreeViewItemName);
        }

        public void Detach()
        {
            RemoveFromClassList(k_UnityListViewItemName);
            RemoveFromClassList(k_UnityTreeViewItemName);
        }

        void AddEntityStyle(HierarchyNode.Immutable node)
        {
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconEntity);

            if (m_Model.GetUnityObject(node.GetHandle()))
            {
                m_PingGameObject.AddToClassList(UssClasses.Hierarchy.Item.VisibleOnHover);
                m_PingGameObject.RegisterCallback<MouseUpEvent>(OnPingGameObject);
            }
        }

        void AddSceneStyle(HierarchyNode.Immutable node)
        {
            m_Name.AddToClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.AddToClassList(UssClasses.Hierarchy.Item.SceneNode);
        }

        void AddSubSceneStyle(HierarchyNode.Immutable node)
        {
            AddToClassList(UssClasses.Hierarchy.Item.SubSceneNode);
            m_Name.AddToClassList(UssClasses.Hierarchy.Item.NameScene);
            m_Icon.AddToClassList(UssClasses.Hierarchy.Item.IconScene);
            m_ContentContainer.AddToClassList(UssClasses.Hierarchy.Item.SceneNode);

            m_ContextMenuManipulator = new ContextualMenuManipulator(null);
            this.AddManipulator(m_ContextMenuManipulator);
            RegisterCallback<ContextualMenuPopulateEvent>(OnSubSceneContextMenu);
        }

        void OnUpdate()
        {
            Refresh();
        }

        void Refresh()
        {
            if (m_Model.World == null)
                return;

            if (Handle.Kind == NodeKind.SubScene)
            {
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
    }
}
