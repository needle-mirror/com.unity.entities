using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Profiling;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;
using ListView = Unity.Editor.Bridge.ListView;
using UnityObject = UnityEngine.Object;

namespace Unity.Entities.Editor
{
    class EntityHierarchy : VisualElement
    {
        [Flags]
        enum ViewMode : byte
        {
            Full    = 0b0001,
            Search  = 0b0010,
            Message = 0b0100
        }

        static readonly ProfilerMarker k_RefreshViewMarker = new ProfilerMarker($"{nameof(EntityHierarchy)}.{nameof(OnUpdate)} > {nameof(RefreshView)}");
        static readonly ProfilerMarker k_RefreshTreeViewMarker = new ProfilerMarker($"{nameof(EntityHierarchy)}.{nameof(OnUpdate)} > {nameof(EntityHierarchyTreeView)}.{nameof(EntityHierarchyTreeView.Refresh)}");
        static readonly ProfilerMarker k_SearchViewFilterItemsMarker = new ProfilerMarker($"{nameof(EntityHierarchy)}.{nameof(RefreshSearchView)} > Apply Search Filter");
        static readonly ProfilerMarker k_SearchViewRefreshUIMarker = new ProfilerMarker($"{nameof(EntityHierarchy)}.{nameof(RefreshSearchView)} > Refresh UI");

        internal static readonly string ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        internal static readonly string ComponentTypeNotFoundContent = L10n.Tr("\"{0}\" is not a component type");
        internal static readonly string NoEntitiesFoundTitle = L10n.Tr("No entity matches your search");

        internal readonly BasicPool<EntityHierarchyItemView> EntityHierarchyViewItemPool = new BasicPool<EntityHierarchyItemView>(() => new EntityHierarchyItemView());
        readonly int[] m_CachedSingleSelectionBuffer = new int[1];

        readonly List<int> m_TreeViewItemsToExpand = new List<int>(128);
        readonly List<EntityHierarchyNodeId> m_ListViewFilteredItems = new List<EntityHierarchyNodeId>(1024);
        readonly VisualElement m_ViewContainer;
        readonly EntityHierarchyTreeView m_TreeView;
        readonly ListView m_ListView;
        readonly CenteredMessageElement m_SearchEmptyMessage;

        IEntityHierarchy m_Hierarchy;
        EntityHierarchyQueryBuilder.Result m_QueryBuilderResult;
        bool m_SearcherCacheNeedsRebuild = true;
        bool m_StructureChanged;
        uint m_RootVersion;
        bool m_QueryChanged;
        ISearchQuery<EntityHierarchyNodeId> m_CurrentQuery;
        EntityHierarchyNodeId m_SelectedItem;
        ViewMode m_CurrentViewMode;
        bool m_CurrentViewModeChanged;
        ViewMode CurrentViewMode
        {
            get => m_CurrentViewMode;
            set
            {
                if (m_CurrentViewMode == value)
                    return;

                m_CurrentViewMode = value;
                m_CurrentViewModeChanged = true;
            }
        }

        HierarchyNamesCache m_NamesCache;
        NativeList<FixedString64Bytes> m_CurrentSearchTokens = new NativeList<FixedString64Bytes>(8, Allocator.Persistent);

        public EntityHierarchy()
        {
            style.flexGrow = 1.0f;
            m_ViewContainer = new VisualElement();
            m_ViewContainer.style.flexGrow = 1.0f;
            m_ViewContainer.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.LeftMouse)
                    Selection.activeObject = null;
            });
            m_TreeView = new EntityHierarchyTreeView(Constants.ListView.ItemHeight, MakeTreeViewItem, ReleaseTreeViewItem, BindTreeViewItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.FullViewName,
                style = { flexGrow = 1 },
                viewDataKey = "full-view",
            };
            m_TreeView.onSelectionChange += OnLocalSelectionChanged;
            m_TreeView.Hide();
            m_ViewContainer.Add(m_TreeView);

            m_ListView = new ListView(m_ListViewFilteredItems, Constants.ListView.ItemHeight, MakeListViewItem, ReleaseListViewItem, BindListViewItem)
            {
                selectionType = SelectionType.Single,
                name = Constants.EntityHierarchy.SearchViewName,
                viewDataKey = "search-view",
                style = { flexGrow = 1 }
            };

            m_ListView.Hide();
            m_ViewContainer.Add(m_ListView);

            m_SearchEmptyMessage = new CenteredMessageElement();
            m_SearchEmptyMessage.Hide();
            Add(m_SearchEmptyMessage);

            m_ListView.onSelectionChange += OnLocalSelectionChanged;

            m_NamesCache = new HierarchyNamesCache();

            CurrentViewMode = ViewMode.Full;

            Add(m_ViewContainer);
            Selection.selectionChanged += OnGlobalSelectionChanged;
        }

        public void Dispose()
        {
            // ReSharper disable once DelegateSubtraction
            Selection.selectionChanged -= OnGlobalSelectionChanged;
            EntityHierarchyItemView.ItemsScheduledForPeriodicCheck.Clear();

            m_CurrentSearchTokens.Dispose();
            m_NamesCache.Dispose();
        }

        public void SetFilter(ISearchQuery<EntityHierarchyNodeId> searchQuery, EntityHierarchyQueryBuilder.Result queryBuilderResult)
        {
            m_QueryBuilderResult = queryBuilderResult;
            m_SearchEmptyMessage.SetVisibility(!queryBuilderResult.IsValid);
            m_ViewContainer.SetVisibility(queryBuilderResult.IsValid);

            if (!queryBuilderResult.IsValid)
            {
                m_SearchEmptyMessage.Title = ComponentTypeNotFoundTitle;
                m_SearchEmptyMessage.Message = string.Format(ComponentTypeNotFoundContent, queryBuilderResult.ErrorComponentType);
                CurrentViewMode = ViewMode.Message;
                return;
            }

            m_CurrentQuery = searchQuery;
            HierarchyItemSearchUtils.PreProcessTokens(searchQuery.Tokens, m_CurrentSearchTokens);

            m_QueryChanged = true;
            var showFilterView = queryBuilderResult.QueryDesc != null || m_CurrentQuery != null && !string.IsNullOrWhiteSpace(m_CurrentQuery.SearchString) && m_CurrentQuery.Tokens.Count != 0;

            CurrentViewMode = showFilterView ? ViewMode.Search : ViewMode.Full;
        }

        public void Refresh(IEntityHierarchy entityHierarchy)
        {
            m_TreeView.UpdateSourceState(entityHierarchy.State);

            if (m_Hierarchy == entityHierarchy)
                return;

            EntityHierarchyItemView.ItemsScheduledForPeriodicCheck.Clear();
            m_Hierarchy = entityHierarchy;

            UpdateStructure();
            OnUpdate();
        }

        public void UpdateStructure()
        {
            // Topology changes will be applied during the next update
            m_StructureChanged = true;
            m_SearcherCacheNeedsRebuild = true;
            m_RootVersion = 0;
        }

        public void OnUpdate()
        {
            if (m_Hierarchy?.GroupingStrategy == null)
                return;

            var rootVersion = m_Hierarchy.State.GetNodeVersion(EntityHierarchyNodeId.Root);
            m_StructureChanged = m_StructureChanged || rootVersion != m_RootVersion;

            if (m_StructureChanged || m_QueryChanged)
            {
                using (k_RefreshViewMarker.Auto())
                {
                    RefreshView();
                }

                if (m_CurrentViewModeChanged)
                    TrySelect(m_SelectedItem);

                m_CurrentViewModeChanged = false;
                m_StructureChanged = false;
                m_RootVersion = rootVersion;
                m_QueryChanged = false;
            }
        }

        void RefreshView()
        {
            if (CurrentViewMode == ViewMode.Full)
                RefreshFullView();
            else if ((CurrentViewMode & ViewMode.Search) == ViewMode.Search)
                RefreshSearchView();

            // Note: m_CurrentViewMode might have changed as a result of calling RefreshSearchView(), if a result is not found
            UpdateViewModeVisibility();
        }

        void UpdateViewModeVisibility()
        {
            switch (CurrentViewMode)
            {
                case ViewMode.Message:
                case ViewMode.Message | ViewMode.Search:
                    m_SearchEmptyMessage.Show();
                    m_TreeView.Hide();
                    m_ListView.Hide();
                    m_ViewContainer.Hide();
                    break;
                case ViewMode.Search:
                    m_SearchEmptyMessage.Hide();
                    m_TreeView.Hide();
                    m_ListView.Show();
                    m_ViewContainer.Show();
                    break;
                default: // (case ViewMode.Full:)
                    m_SearchEmptyMessage.Hide();
                    m_ListView.Hide();
                    m_TreeView.Show();
                    m_ViewContainer.Show();
                    break;
            }
        }

        void RefreshFullView()
        {
            if (m_StructureChanged)
                RecreateTreeViewItemsToExpand();

            m_TreeView.PrepareItemsToExpand(m_TreeViewItemsToExpand);

            using (k_RefreshTreeViewMarker.Auto())
                m_TreeView.Refresh();
        }

        void RecreateTreeViewItemsToExpand()
        {
            m_TreeViewItemsToExpand.Clear();
            foreach (var rootItem in m_Hierarchy.State.GetChildren(EntityHierarchyNodeId.Root))
            {
                if (rootItem.Kind != NodeKind.RootScene)
                    continue;

                m_TreeViewItemsToExpand.Add(rootItem.HashCode);

                if (!m_Hierarchy.State.TryGetChildren(rootItem, out var children))
                    continue;

                foreach (var childItem in children)
                {
                    if (childItem.Kind != NodeKind.SubScene)
                        continue;

                    m_TreeViewItemsToExpand.Add(childItem.HashCode);
                }
            }
        }

        void RefreshSearchView()
        {
            if (m_SearcherCacheNeedsRebuild)
            {
                m_NamesCache.Rebuild(m_Hierarchy.State);
                m_SearcherCacheNeedsRebuild = false;
            }

            m_ListViewFilteredItems.Clear();

            k_SearchViewFilterItemsMarker.Begin();

            HierarchyItemSearchUtils.FilterByName(m_Hierarchy.State.GetAllNodesOrdered(), m_NamesCache.Names, m_CurrentSearchTokens, m_ListViewFilteredItems);

            k_SearchViewFilterItemsMarker.End();

            using (k_SearchViewRefreshUIMarker.Auto())
            {
                if (m_ListViewFilteredItems.Count == 0 && m_QueryBuilderResult.IsValid)
                {
                    m_SearchEmptyMessage.Title = NoEntitiesFoundTitle;
                    m_SearchEmptyMessage.Message = string.Empty;
                    CurrentViewMode |= ViewMode.Message;
                }
                else if ((CurrentViewMode & ViewMode.Message) == ViewMode.Message)
                {
                    CurrentViewMode &= ~ViewMode.Message;
                }

                m_ListView.Refresh();
            }
        }

        // Suppressing this warning: `TryX` is a common pattern that always returns a bool
        // ReSharper disable once UnusedMethodReturnValue.Local
        bool TrySelect(EntityHierarchyNodeId id)
        {
            if (id == default || !m_Hierarchy.State.Exists(id))
            {
                if (m_SelectedItem != default)
                    Deselect();

                return false;
            }

            Select(id);
            return true;
        }

        void Select(EntityHierarchyNodeId id)
        {
            m_SelectedItem = id;
            switch (CurrentViewMode)
            {
                case ViewMode.Full:
                {
                    m_TreeView.Select(id.GetHashCode(), false);
                    break;
                }
                case ViewMode.Search:
                {
                    var index = m_ListViewFilteredItems.FindIndex(item => item == id);
                    if (index != -1)
                    {
                        m_ListView.ScrollToItem(index);
                        m_CachedSingleSelectionBuffer[0] = index;
                        m_ListView.SetSelectionWithoutNotify(m_CachedSingleSelectionBuffer);
                    }

                    break;
                }
            }
        }

        void Deselect()
        {
            m_SelectedItem = default;
            m_TreeView.ClearSelection();
            m_ListView.ClearSelection();
        }

        void OnLocalSelectionChanged(IEnumerable<object> selection)
        {
            if (selection.FirstOrDefault() is EntityHierarchyNodeId selectedItem)
                OnLocalSelectionChanged(selectedItem);
        }

        void OnLocalSelectionChanged(IEnumerable<EntityHierarchyNodeId> selection)
        {
            using var enumerator = selection.GetEnumerator();
            if (enumerator.MoveNext())
                OnLocalSelectionChanged(enumerator.Current);
        }

        void OnLocalSelectionChanged(EntityHierarchyNodeId selectedItem)
        {
            m_SelectedItem = selectedItem;
            if (selectedItem.Kind == NodeKind.Entity)
            {
                var entity = selectedItem.ToEntity();
                if (entity != Entity.Null)
                {
                    var undoGroup = Undo.GetCurrentGroup();
                    EntitySelectionProxy.SelectEntity(m_Hierarchy.World, entity);

                    // Collapsing the selection of the entity into the selection of the ListView / TreeView item
                    Undo.CollapseUndoOperations(undoGroup);
                }
            }
            else
            {
                // TODO: Deal with non-Entity selections
            }
        }

        void OnGlobalSelectionChanged()
        {
            if (Selection.activeObject is EntitySelectionProxy selectedProxy && selectedProxy.World == m_Hierarchy.World)
                TrySelect(EntityHierarchyNodeId.FromEntity(selectedProxy.Entity));
            else
                Deselect();
        }

        VisualElement MakeTreeViewItem() => EntityHierarchyViewItemPool.Acquire();

        void ReleaseTreeViewItem(VisualElement ve)
        {
            var entityHierarchyItemView = ((EntityHierarchyItemView)ve);
            entityHierarchyItemView.Reset();
            EntityHierarchyViewItemPool.Release(entityHierarchyItemView);
        }

        VisualElement MakeListViewItem()
        {
            // ListView changes user created VisualElements in a way that no reversible using public API
            // Wrapping pooled item in a non reusable container prevent us from reusing a pooled item in an eventual checked pseudo state
            var wrapper = new VisualElement();
            wrapper.Add(EntityHierarchyViewItemPool.Acquire());
            return wrapper;
        }

        void ReleaseListViewItem(VisualElement ve)
        {
            var entityHierarchyItemView = (EntityHierarchyItemView) ve[0];
            entityHierarchyItemView.Reset();
            EntityHierarchyViewItemPool.Release(entityHierarchyItemView);
        }

        void BindTreeViewItem(VisualElement element, EntityHierarchyNodeId item)
            => ((EntityHierarchyItemView)element).SetSource(item, m_Hierarchy);

        void BindListViewItem(VisualElement element, int itemIndex)
            => BindTreeViewItem(element[0], (EntityHierarchyNodeId)m_ListView.itemsSource[itemIndex]);
    }
}
