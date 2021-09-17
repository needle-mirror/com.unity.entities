using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Editor.Bridge;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;

namespace Unity.Entities.Editor
{
    class EntityHierarchyTreeView : VisualElementBridge, ISerializationCallbackReceiver
    {
        const string k_ListViewName = "unity-tree-view__list-view";
        const string k_ItemName = "unity-tree-view__item";
        const string k_ItemToggleName = "unity-tree-view__item-toggle";
        const string k_ItemIndentsContainerName = "unity-tree-view__item-indents";
        const string k_ItemIndentName = "unity-tree-view__item-indent";
        const string k_ItemContentContainerName = "unity-tree-view__item-content";

        static readonly ProfilerMarker k_RegenerateWrappersMarker = new ProfilerMarker($"{nameof(EntityHierarchyTreeView)}.{nameof(RegenerateWrappers)}()");
        static readonly ProfilerMarker k_RefreshListViewMarker = new ProfilerMarker($"{nameof(EntityHierarchyTreeView)} Refresh ListView");

        // ReSharper disable once InconsistentNaming
        public event Action<IEnumerable<EntityHierarchyNodeId>> onSelectionChange;

        readonly List<EntityHierarchyNodeId> m_VisibleItems;

        // Used in tests
        public IEnumerable<EntityHierarchyNodeId> VisibleItems => m_VisibleItems;

        // R# is confused here: this attribute is indeed required
        // ReSharper disable once Unity.RedundantSerializeFieldAttribute
        [SerializeField]
        List<int> m_SerializedExpandedItemIds;
        HashSet<int> m_ExpandedItems;

        readonly ListView m_ListView;
        readonly ScrollView m_ScrollView;

        // Avoids recreating a collection each time we want to select a single item (see: Select(int, bool))
        readonly int[] m_CachedSingleSelectionBuffer = new int[1];

        readonly Func<VisualElement> m_MakeItem;
        readonly Action<VisualElement> m_ReleaseItem;
        readonly Action<VisualElement, EntityHierarchyNodeId> m_BindItem;

        IEntityHierarchyState m_EntityHierarchyState;

        List<EntityHierarchyNodeId> m_SelectedItems;

        // ReSharper disable once InconsistentNaming
        public IEnumerable<EntityHierarchyNodeId> items => m_EntityHierarchyState.GetAllNodesOrdered();

        // ReSharper disable once InconsistentNaming
        public SelectionType selectionType
        {
            get => m_ListView.selectionType;
            set => m_ListView.selectionType = value;
        }

        // ReSharper disable once InconsistentNaming
        public new string viewDataKey
        {
            get => base.viewDataKey;
            set
            {
                base.viewDataKey = value;
                m_ListView.viewDataKey = value;
            }
        }

        public EntityHierarchyTreeView(
            int itemHeight,
            Func<VisualElement> makeItem,
            Action<VisualElement> releaseItem,
            Action<VisualElement, EntityHierarchyNodeId> bindItem)
        {
            m_SelectedItems = null;
            m_VisibleItems = new List<EntityHierarchyNodeId>();
            m_ExpandedItems = new HashSet<int>();

            m_ListView = new ListView
            {
                name = k_ListViewName,
                itemsSource = m_VisibleItems
            };

            m_ListView.AddToClassList(k_ListViewName);
            hierarchy.Add(m_ListView);

            m_ListView.makeItem = MakeTreeItem;
            m_ListView.releaseItem = ReleaseTreeItem;
            m_ListView.bindItem = BindTreeItem;
            m_ListView.getItemId = GetItemId;

            m_ScrollView = m_ListView.m_ScrollView;
            m_ScrollView.contentContainer.RegisterCallback<KeyDownEvent>(OnKeyDown);
            m_ListView.onSelectionChange += OnSelectionChange;

            m_ListView.itemHeight = itemHeight;
            m_MakeItem = makeItem;
            m_ReleaseItem = releaseItem;
            m_BindItem = bindItem;

            RegisterCallback<MouseUpEvent>(OnTreeViewMouseUp, TrickleDown.TrickleDown);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        public void Select(int id, bool sendNotification)
        {
            var index = GetItemIndex(id, true);
            Refresh();
            m_ListView.ScrollToItem(index);

            m_CachedSingleSelectionBuffer[0] = index;
            m_ListView.SetSelectionInternal(m_CachedSingleSelectionBuffer, sendNotification);
        }

        public void ClearSelection() => m_ListView.ClearSelection();

        public void Refresh()
        {
            if (m_EntityHierarchyState == null)
                return;

            using (k_RegenerateWrappersMarker.Auto())
                RegenerateWrappers();

            using (k_RefreshListViewMarker.Auto())
                m_ListView.Refresh();
        }

        public void PrepareItemsToExpand(List<int> ids)
        {
            if (ids == null || ids.Count == 0)
                return;

            foreach (var id in ids)
            {
                m_ExpandedItems.Add(id);
            }
        }

        public void UpdateSourceState(IEntityHierarchyState newState)
        {
            if (m_EntityHierarchyState == newState)
                return;

            m_EntityHierarchyState = newState;

            // These items are no longer valid
            m_ExpandedItems.Clear();
        }

        // Note: does not invoke any callbacks
        public void FastExpandAll()
        {
            foreach (var item in m_EntityHierarchyState.GetAllNodesUnordered())
            {
                if (m_EntityHierarchyState.HasChildren(item))
                    m_ExpandedItems.Add(item.HashCode);
            }

            Refresh();
        }

        // Note: does not invoke any callbacks
        public void FastCollapseAll()
        {
            m_ExpandedItems.Clear();
            Refresh();
        }

        int GetItemId(int index) => m_VisibleItems[index].HashCode;

        int GetItemIndex(int id, bool expand = false)
        {
            var item = FindItem(id);
            if (item.Equals(default))
                throw new ArgumentOutOfRangeException(nameof(id), id, L10n.Tr("Item id not found."));

            if (expand)
            {
                var regenerateWrappers = false;
                var currentDepth = m_EntityHierarchyState.GetDepth(item);
                var currentItemNodeId = item;
                while (currentDepth-- > 0)
                {
                    currentItemNodeId = m_EntityHierarchyState.GetParent(currentItemNodeId);

                    if (m_ExpandedItems.Add(currentItemNodeId.HashCode))
                        regenerateWrappers = true;
                }

                if (regenerateWrappers)
                    RegenerateWrappers();
            }

            var index = 0;
            for (; index < m_VisibleItems.Count; ++index)
                if (m_VisibleItems[index].HashCode == id)
                    break;

            return index;
        }

        EntityHierarchyNodeId FindItem(int id)
        {
            foreach (var item in m_EntityHierarchyState.GetAllNodesUnordered())
                if (item.HashCode == id)
                    return item;

            return default;
        }

        bool IsExpandedByIndex(int index) => m_ExpandedItems.Contains(m_VisibleItems[index].HashCode);

        void CollapseItemByIndex(int index)
        {
            if (!m_EntityHierarchyState.HasChildren(m_VisibleItems[index]))
                return;

            m_ExpandedItems.Remove(m_VisibleItems[index].HashCode);

            var recursiveChildCount = 0;
            var currentIndex = index + 1;
            var currentDepth = m_EntityHierarchyState.GetDepth(m_VisibleItems[index]);
            while (currentIndex < m_VisibleItems.Count && m_EntityHierarchyState.GetDepth(m_VisibleItems[currentIndex]) > currentDepth)
            {
                recursiveChildCount++;
                currentIndex++;
            }

            m_VisibleItems.RemoveRange(index + 1, recursiveChildCount);

            m_ListView.Refresh();

            SaveViewData();
        }

        void ExpandItemByIndex(int index)
        {
            if (!m_EntityHierarchyState.TryGetChildren(m_VisibleItems[index], out var children))
                return;

            using (var childWrappers = PooledList<EntityHierarchyNodeId>.Make())
            {
                CreateWrappers(children, childWrappers);
                m_VisibleItems.InsertRange(index + 1, childWrappers.List);
            }

            m_ExpandedItems.Add(m_VisibleItems[index].HashCode);

            m_ListView.Refresh();

            SaveViewData();
        }

        void ToggleExpandedState(ChangeEvent<bool> evt)
        {
            var index = (int)((Toggle) evt.target).userData;
            var isExpanded = IsExpandedByIndex(index);

            Assert.AreNotEqual(isExpanded, evt.newValue);

            if (isExpanded)
                CollapseItemByIndex(index);
            else
                ExpandItemByIndex(index);

            // To make sure our TreeView gets focus, we need to force this. :(
            m_ScrollView.contentContainer.Focus();
        }

        void RegenerateWrappers()
        {
            m_VisibleItems.Clear();

            if (m_EntityHierarchyState.TryGetChildren(EntityHierarchyNodeId.Root, out var children))
                CreateWrappers(children, m_VisibleItems);
        }

        void CreateWrappers(HashSet<EntityHierarchyNodeId> sourceItems, List<EntityHierarchyNodeId> visibleItems)
        {
            //Depth First Search
            foreach (var item in sourceItems)
            {
                visibleItems.Add(item);

                if (m_ExpandedItems.Contains(item.HashCode) && m_EntityHierarchyState.TryGetChildren(item, out var children))
                    CreateWrappers(children, visibleItems);
            }
        }

        VisualElement MakeTreeItem()
        {
            var itemContainer = new VisualElement()
            {
                name = k_ItemName,
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };
            itemContainer.AddToClassList(k_ItemName);
            itemContainer.RegisterCallback<MouseUpEvent>(OnItemMouseUp);

            var indents = new VisualElement()
            {
                name = k_ItemIndentsContainerName,
                style =
                {
                    flexDirection = FlexDirection.Row
                }
            };
            indents.AddToClassList(k_ItemIndentsContainerName);
            itemContainer.hierarchy.Add(indents);

            var toggle = new Toggle() { name = k_ItemToggleName };
            toggle.AddToClassList(Foldout.toggleUssClassName);
            toggle.RegisterValueChangedCallback(ToggleExpandedState);
            itemContainer.hierarchy.Add(toggle);

            var userContentContainer = new VisualElement()
            {
                name = k_ItemContentContainerName,
                style =
                {
                    flexGrow = 1,
                    flexShrink = 1
                }
            };
            userContentContainer.AddToClassList(k_ItemContentContainerName);
            itemContainer.Add(userContentContainer);

            if (m_MakeItem != null)
                userContentContainer.Add(m_MakeItem());

            return itemContainer;
        }

        void ReleaseTreeItem(VisualElement ve) => m_ReleaseItem?.Invoke(ve.Q(k_ItemContentContainerName)[0]);

        void BindTreeItem(VisualElement element, int index)
        {
            var item = m_VisibleItems[index];

            // Add indentation.
            var indents = element.Q(k_ItemIndentsContainerName);
            indents.Clear();
            var depth = m_EntityHierarchyState.GetDepth(m_VisibleItems[index]);
            for (var i = 0; i < depth; ++i)
            {
                var indentElement = new VisualElement();
                indentElement.AddToClassList(k_ItemIndentName);
                indents.Add(indentElement);
            }

            // Set toggle data.
            var toggle = element.Q<Toggle>(k_ItemToggleName);
            toggle.SetValueWithoutNotify(IsExpandedByIndex(index));
            toggle.userData = index;
            toggle.visible = m_EntityHierarchyState.HasChildren(item);

            if (m_BindItem == null)
                return;

            // Bind user content container.
            var userContentContainer = element.Q(k_ItemContentContainerName).ElementAt(0);
            m_BindItem(userContentContainer, item);
        }

        internal override void OnViewDataReady()
        {
            if (m_EntityHierarchyState == null)
                return;

            base.OnViewDataReady();
            var key = GetFullHierarchicalViewDataKey();
            OverwriteFromViewData(this, key);
            Refresh();
        }

        void OnSelectionChange(IEnumerable<object> selectedListItems)
        {
            m_SelectedItems ??= new List<EntityHierarchyNodeId>();

            m_SelectedItems.Clear();
            foreach (var item in selectedListItems)
                m_SelectedItems.Add((EntityHierarchyNodeId)item);

            onSelectionChange?.Invoke(m_SelectedItems);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            var index = m_ListView.selectedIndex;
            var shouldStopPropagation = true;

            switch (evt.keyCode)
            {
                case KeyCode.RightArrow:
                    if (!IsExpandedByIndex(index))
                        ExpandItemByIndex(index);
                    break;
                case KeyCode.LeftArrow:
                    if (IsExpandedByIndex(index))
                        CollapseItemByIndex(index);
                    break;
                default:
                    shouldStopPropagation = false;
                    break;
            }

            if (shouldStopPropagation)
                evt.StopPropagation();
        }

        void OnTreeViewMouseUp(MouseUpEvent evt) => m_ScrollView.contentContainer.Focus();

        void OnItemMouseUp(MouseUpEvent evt)
        {
            if ((evt.modifiers & EventModifiers.Alt) == 0)
                return;

            var target = evt.currentTarget as VisualElement;
            var toggle = target.Q<Toggle>(k_ItemToggleName);
            var index = (int)toggle.userData;
            var item = m_VisibleItems[index];
            var wasExpanded = IsExpandedByIndex(index);

            if (!m_EntityHierarchyState.HasChildren(item))
                return;

            if (wasExpanded)
                m_ExpandedItems.Remove(item.HashCode);
            else
                m_ExpandedItems.Add(item.HashCode);

            foreach (var child in m_EntityHierarchyState.GetAllDescendants(item))
            {
                if (m_EntityHierarchyState.HasChildren(child))
                {
                    if (wasExpanded)
                        m_ExpandedItems.Remove(child.HashCode);
                    else
                        m_ExpandedItems.Add(child.HashCode);
                }
            }

            Refresh();

            evt.StopPropagation();
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e)
        {
            var oldHeight = m_ListView.itemHeight;
            if (!m_ListView.m_ItemHeightIsInline && e.customStyle.TryGetValue(ListView.s_ItemHeightProperty, out var height))
                m_ListView.m_ItemHeight = height;

            if (m_ListView.m_ItemHeight != oldHeight)
                m_ListView.Refresh();
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
            if (m_ExpandedItems != null)
                m_SerializedExpandedItemIds = m_ExpandedItems.ToList();
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_SerializedExpandedItemIds != null)
                m_ExpandedItems = new HashSet<int>(m_SerializedExpandedItemIds);
        }
    }
}
