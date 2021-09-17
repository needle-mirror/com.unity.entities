using JetBrains.Annotations;
using Unity.Profiling;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class EntityHierarchyWindow : DOTSEditorWindow, IEntityHierarchy, IHasCustomMenu
    {
        static readonly ProfilerMarker k_OnUpdateMarker = new ProfilerMarker($"{nameof(EntityHierarchyWindow)}.{nameof(OnUpdate)}");

        static readonly string k_WindowName = L10n.Tr("Entities");
        static readonly string k_FilterComponentType = L10n.Tr("Component type");
        static readonly string k_FilterComponentTypeTooltip = L10n.Tr("Filter entities that have the specified component type");

        readonly EntityHierarchyQueryBuilder m_EntityHierarchyQueryBuilder = new EntityHierarchyQueryBuilder();

        EntityHierarchy m_EntityHierarchy;
        EntityHierarchyDiffer m_EntityHierarchyDiffer;
        VisualElement m_EnableLiveConversionMessage;
        VisualElement m_Header;
        VisualElement m_Root;
        SearchElement m_SearchElement;
        CenteredMessageElement m_NoWorld;

        public IEntityHierarchyState State { get; private set; }
        public IEntityHierarchyGroupingStrategy GroupingStrategy { get; private set; }
        public EntityQueryDesc QueryDesc { get; private set; }
        public World World { get; private set; }

        // Internal for tests
        internal bool DisableDifferCooldownPeriod { get; set; }

        void OnEnable()
        {
            rootVisualElement.Add(new HelpBox("The Entities window is deprecated and will be removed in a future release.\nThe window has been replaced with Window > DOTS > Hierarchy", HelpBoxMessageType.Info));

            Resources.AddCommonVariables(rootVisualElement);

            titleContent = new GUIContent(k_WindowName, EditorIcons.EntityGroup);
            minSize = Constants.MinWindowSize;

            m_Root = new VisualElement { style = { flexGrow = 1 } };
            rootVisualElement.Add(m_Root);

            m_NoWorld = new CenteredMessageElement() { Message = NoWorldMessageContent };
            rootVisualElement.Add(m_NoWorld);
            m_NoWorld.Hide();

            Resources.Templates.DotsEditorCommon.AddStyles(m_Root);

            CreateToolbar();
            m_EntityHierarchy = new EntityHierarchy { viewDataKey = nameof(EntityHierarchy) };
            m_Root.Add(m_EntityHierarchy);
            CreateEnableLiveConversionMessage();

            m_EntityHierarchy.Refresh(this);

            m_SearchElement.Search(SearchFilter);

            LiveConversionConfigHelper.LiveConversionEnabledChanged += UpdateEnableLiveConversionMessage;
            EditorApplication.playModeStateChanged += UpdateEnableLiveConversionMessage;

            rootVisualElement.schedule.Execute(EntityHierarchyItemViewPeriodicCheck).Every(EntityHierarchyItemView.DisabledStateRefreshPeriodInMs);
        }

        // internal for tests
        internal void EntityHierarchyItemViewPeriodicCheck()
        {
            using var itemsToRemove = PooledList<EntityHierarchyItemView>.Make();
            foreach (var item in EntityHierarchyItemView.ItemsScheduledForPeriodicCheck)
            {
                if (!item.TryPerformPeriodicCheck(this))
                    itemsToRemove.List.Add(item);
            }

            foreach (var itemView in itemsToRemove.List)
            {
                EntityHierarchyItemView.ItemsScheduledForPeriodicCheck.Remove(itemView);
            }
        }

        protected override void OnWorldsChanged(bool containsAnyWorld)
        {
            m_Root.SetVisibility(containsAnyWorld);
            m_NoWorld.SetVisibility(!containsAnyWorld);
        }

        void OnDisable()
        {
            LiveConversionConfigHelper.LiveConversionEnabledChanged -= UpdateEnableLiveConversionMessage;
            EditorApplication.playModeStateChanged -= UpdateEnableLiveConversionMessage;

            State?.Dispose();
            GroupingStrategy?.Dispose();
            m_EntityHierarchyDiffer?.Dispose();
            m_EntityHierarchy.Dispose();
        }

        void UpdateEnableLiveConversionMessage()
        {
            m_EnableLiveConversionMessage.SetVisibility(!EditorApplication.isPlaying && !LiveConversionConfigHelper.LiveConversionEnabledInEditMode);
            m_EntityHierarchy.SetVisibility(EditorApplication.isPlaying || LiveConversionConfigHelper.LiveConversionEnabledInEditMode);
            m_Header.SetVisibility(EditorApplication.isPlaying || LiveConversionConfigHelper.LiveConversionEnabledInEditMode);
        }

        void UpdateEnableLiveConversionMessage(PlayModeStateChange _)
            => UpdateEnableLiveConversionMessage();

        void CreateToolbar()
        {
            m_Header = new VisualElement();
            Resources.Templates.EntityHierarchyToolbar.Clone(m_Header);
            var leftSide = m_Header.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Toolbar.LeftSide);
            var rightSide = m_Header.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Toolbar.RightSide);
            leftSide.Add(CreateWorldSelector());

            AddSearchIcon(rightSide, UssClasses.DotsEditorCommon.SearchIcon);
            m_SearchElement = AddSearchElement<EntityHierarchyNodeId>(m_Header, UssClasses.DotsEditorCommon.SearchFieldContainer);
            m_SearchElement.RegisterSearchQueryHandler<EntityHierarchyNodeId>(query =>
            {
                var result = m_EntityHierarchyQueryBuilder.BuildQuery(query.SearchString);
                QueryDesc = result.QueryDesc;

                m_EntityHierarchy.SetFilter(query, result);
            });
            m_SearchElement.AddSearchFilterPopupItem(Constants.EntityHierarchy.ComponentToken, k_FilterComponentType, k_FilterComponentTypeTooltip);
            m_SearchElement.EnableAutoComplete(ComponentTypeAutoComplete.Instance);

            m_Root.Add(m_Header);
        }

        void CreateEnableLiveConversionMessage()
        {
            m_EnableLiveConversionMessage = new VisualElement { style = { flexGrow = 1 } };
            Resources.Templates.EntityHierarchyEnableLiveConversionMessage.Clone(m_EnableLiveConversionMessage);
            m_EnableLiveConversionMessage.Q<Button>().clicked += () => LiveConversionConfigHelper.LiveConversionEnabledInEditMode = true;
            m_Root.Add(m_EnableLiveConversionMessage);

            UpdateEnableLiveConversionMessage();
        }

        protected override void OnWorldSelected(World world)
        {
            if (world == World)
                return;

            State?.Dispose();
            GroupingStrategy?.Dispose();
            m_EntityHierarchyDiffer?.Dispose();

            State = null;
            GroupingStrategy = null;
            m_EntityHierarchyDiffer = null;

            World = world;

            if (World != null)
            {
                // TODO: How do we instantiate the correct State/Strategy combination?
                // TODO: Should we even allow the State to be overridable by users?
                State = new EntityHierarchyState(world);
                GroupingStrategy = new EntityHierarchyDefaultGroupingStrategy(world, State);

                m_EntityHierarchyDiffer = new EntityHierarchyDiffer(this, DisableDifferCooldownPeriod ? 0 : 16);
            }

            m_EntityHierarchy.Refresh(this);
        }

        protected override void OnUpdate()
        {
            using (k_OnUpdateMarker.Auto())
            {
                if (World == null || !m_EntityHierarchyDiffer.TryUpdate(out var structuralChangeDetected))
                    return;

                if (structuralChangeDetected)
                    m_EntityHierarchy.UpdateStructure();

                m_EntityHierarchy.OnUpdate();
            }
        }

        class TelemetryContent : ContentProvider
        {
            public override string Name => "Entities (Debug)";
            public override object GetContent() => new EntityWindowTelemetryData(m_Context);
            readonly EntityHierarchyWindow m_Context;

            public TelemetryContent() { }

            public TelemetryContent(EntityHierarchyWindow context) => m_Context = context;
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            if (Unsupported.IsDeveloperMode())
            {
                menu.AddItem(new GUIContent("Debug..."), false, () =>
                    SelectionUtility.ShowInWindow(new TelemetryContent(this)));
            }
        }

        class EntityWindowTelemetryData
        {
            readonly EntityHierarchyWindow m_Context;

            public EntityWindowTelemetryData(EntityHierarchyWindow context)
            {
                m_Context = context;
            }

            [CreateProperty, UsedImplicitly] public int EntityHierarchyItemViewActiveInstanceCount => m_Context.m_EntityHierarchy.EntityHierarchyViewItemPool.ActiveInstanceCount;
            [CreateProperty, UsedImplicitly] public int EntityHierarchyItemViewPoolSize => m_Context.m_EntityHierarchy.EntityHierarchyViewItemPool.PoolSize;

            [CreateProperty, UsedImplicitly]
            public int EntityHierarchyItemViewItemsScheduledForPeriodicCheck => EntityHierarchyItemView.ItemsScheduledForPeriodicCheck != null ? EntityHierarchyItemView.ItemsScheduledForPeriodicCheck.Count : 0;

            [UsedImplicitly]
            class Inspector : Inspector<EntityWindowTelemetryData>
            {
                public override VisualElement Build()
                {
                    var root = Resources.Templates.EntityDebugWindow.Clone();
                    Resources.Templates.DebugWindow.AddStyles(root);
                    return root;
                }
            }
        }
    }
}
