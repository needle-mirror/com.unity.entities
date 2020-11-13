using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.Serialization;

namespace Unity.Entities.Editor
{
    internal class EntityDebugger : EditorWindow
    {
        internal static ComponentSystemBase[] extraSystems;

        internal static void SetExtraSystems(ComponentSystemBase[] systems)
        {
            if (systems == null)
            {
                extraSystems = null;
            }
            else
            {
                extraSystems = new ComponentSystemBase[systems.Length];
                Array.Copy(systems, extraSystems, systems.Length);
            }
            EntityDebugger.Instance.CreateSystemListView();
        }

        private const float kSystemListWidth = 400f;
        private const float kChunkInfoViewWidth = 250f;

        public bool ShowingChunkInfoView
        {
            get { return showingChunkInfoView; }
            set
            {
                if (showingChunkInfoView != value)
                {
                    showingChunkInfoView = value;
                    if (!showingChunkInfoView)
                    {
                        chunkInfoListView.ClearSelection();
                    }
                }
            }
        }

        private bool showingChunkInfoView = true;

        const string k_ShowChunkInfoPreferencePath = "Unity.Entities.Editor.EntityDebugger.ShowChunkInfo";
        bool ShowingChunkInfoViewPersistent
        {
            get => EditorPrefs.GetBool(k_ShowChunkInfoPreferencePath, true);
            set => EditorPrefs.SetBool(k_ShowChunkInfoPreferencePath, value);
        }

        private float CurrentEntityViewWidth =>
            Mathf.Max(100f, position.width - kSystemListWidth - (showingChunkInfoView ? kChunkInfoViewWidth : 0f));

        [MenuItem("Window/Analysis/Entity Debugger", false)]
        private static void OpenWindow()
        {
            GetWindow<EntityDebugger>("Entity Debugger");
        }

        class DebuggerStyles
        {
            public GUIStyle ToolbarStyle;
            public GUIStyle SearchFieldStyle;
            public GUIStyle SearchFieldCancelButton;
            public GUIStyle SearchFieldCancelButtonEmpty;
            public int SearchFieldWidth;
            public GUIStyle ToolbarDropdownStyle;
            public GUIStyle ToolbarButtonStyle;
            public GUIStyle LabelStyle;
            public GUIStyle BoxStyle;
            public GUIStyle ToolbarLabelStyle;
        }

        private static DebuggerStyles Styles;

        void InitStyles()
        {
            if (Styles == null)
            {
                Styles = new DebuggerStyles();
                Styles.ToolbarStyle = "Toolbar";
                Styles.SearchFieldStyle = "ToolbarSeachTextField";
                Styles.SearchFieldCancelButton = "ToolbarSeachCancelButton";
                Styles.SearchFieldCancelButtonEmpty = "ToolbarSeachCancelButtonEmpty";
                Styles.SearchFieldWidth = 100;
                Styles.ToolbarDropdownStyle = "ToolbarDropDown";
                Styles.ToolbarButtonStyle = "toolbarbutton";
                Styles.LabelStyle = new GUIStyle(EditorStyles.label)
                {
                    margin = EditorStyles.boldLabel.margin,
                    richText = true
                };
                Styles.BoxStyle = new GUIStyle(GUI.skin.box)
                {
                    margin = new RectOffset(),
                    padding = new RectOffset(1, 0, 1, 0),
                    overflow = new RectOffset(0, 1, 0, 1)
                };
                Styles.ToolbarLabelStyle = new GUIStyle(Styles.ToolbarButtonStyle)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleLeft
                };
                var styleState = Styles.ToolbarLabelStyle.normal;
                styleState.background = null;
                styleState.scaledBackgrounds = null;
            }
        }

        public SystemSelection SystemSelection { get; private set; }

        public World SystemSelectionWorld
        {
            get { return systemSelectionWorld?.IsCreated == true ? systemSelectionWorld : null; }
            private set { systemSelectionWorld = value; }
        }

        public void SetSystemSelection(SystemSelection sel, World world, bool updateList, bool propagate)
        {
            if (sel.Managed != null && world == null)
                throw new ArgumentNullException("System cannot have null world");

            SystemSelection = sel;
            SystemSelectionWorld = world;
            if (updateList)
                systemListView.SetSystemSelection(sel);
            CreateEntityQueryListView();
            if (propagate)
            {
                if (SystemSelection.Valid)
                    entityQueryListView.TouchSelection();
                else
                    ApplyAllEntitiesFilter();
            }
        }

        public EntityListQuery EntityListQuerySelection { get; private set; }

        public void SetEntityListSelection(EntityListQuery newSelection, bool updateList, bool propagate)
        {
            chunkInfoListView.ClearSelection();
            EntityListQuerySelection = newSelection;
            if (updateList)
                entityQueryListView.SetEntityListSelection(newSelection);
            entityListView.SelectedEntityQuery = newSelection;
            if (propagate)
                entityListView.TouchSelection();
        }

        public Entity EntitySelection
        {
            get
            {
                if (WorldSelection == null || entityListView == null)
                    return Entity.Null;

                return entityListView.GetSelectedEntity();
            }
        }

        internal void SetEntitySelection(Entity newSelection, bool updateList)
        {
            if (updateList)
                entityListView.SetEntitySelection(newSelection);

            var world = WorldSelection ?? SystemSelection.World;
            if (world != null && newSelection != Entity.Null)
            {
                // No need to re-select the same entity twice
                var selectedProxy = Selection.activeObject as EntitySelectionProxy;
                if (selectedProxy == null || selectedProxy.World != world || selectedProxy.Entity != newSelection)
                {
                    EntitySelectionProxy.SelectEntity(world, newSelection);
                }
            }
            else if (Selection.activeObject is EntitySelectionProxy)
            {
                Selection.activeObject = null;
            }
        }

        internal static void SetAllSelections(World world, ComponentSystemBase system, EntityListQuery entityQuery,
            Entity entity)
        {
            if (Instance == null)
                OpenWindow();
            Instance.SetWorldSelection(world, false);
            Instance.SetSystemSelection(system, world, true, false);
            Instance.SetEntityListSelection(entityQuery, true, false);
            Instance.SetEntitySelection(entity, true);
            Instance.entityListView.FrameSelection();
        }

        private static EntityDebugger Instance { get; set; }

        [FormerlySerializedAs("componentGroupListStates")]
        [SerializeField] private List<TreeViewState> entityQueryListStates = new List<TreeViewState>();
        [FormerlySerializedAs("componentGroupListStateNames")]
        [SerializeField] private List<string> entityQueryListStateNames = new List<string>();
        private EntityQueryListView entityQueryListView;

        [SerializeField] private List<TreeViewState> systemListStates = new List<TreeViewState>();
        [SerializeField] private List<string> systemListStateNames = new List<string>();
        internal SystemListView systemListView;

        [SerializeField] private TreeViewState entityListState = new TreeViewState();
        private EntityListView entityListView;

        [SerializeField] private ChunkInfoListView.State chunkInfoListState = new ChunkInfoListView.State();
        private ChunkInfoListView chunkInfoListView;

        internal WorldPopup m_WorldPopup;

        private ComponentTypeFilterUI filterUI;

        public World WorldSelection
        {
            get
            {
                if (worldSelection != null && worldSelection.IsCreated)
                    return worldSelection;
                return null;
            }
        }

        [SerializeField] private string lastEditModeWorldSelection = WorldPopup.kNoWorldName;
        [SerializeField] private string lastPlayModeWorldSelection = WorldPopup.kNoWorldName;
        [SerializeField] private bool showingPlayerLoop;

        public void SetWorldSelection(World selection, bool propagate)
        {
            if (worldSelection != selection)
            {
                worldSelection = selection;
                showingPlayerLoop = worldSelection == null;
                if (worldSelection != null)
                {
                    if (EditorApplication.isPlaying)
                        lastPlayModeWorldSelection = worldSelection.Name;
                    else
                        lastEditModeWorldSelection = worldSelection.Name;
                }

                CreateSystemListView();
                if (propagate)
                    systemListView.TouchSelection();
            }
        }

        public void SetEntityListChunkFilter(ChunkFilter filter)
        {
            entityListView.SetFilter(filter);
        }

        bool HasWorld() => SystemSelectionWorld != null || WorldSelection != null;

        private void CreateEntityListView()
        {
            entityListView?.Dispose();

            entityListView = new EntityListView(
                entityListState,
                EntityListQuerySelection,
                x => SetEntitySelection(x, false),
                () => SystemSelectionWorld ?? WorldSelection,
                () => SystemSelection,
                x => chunkInfoListView.SetChunkArray(x)
            );
        }

        private void CreateSystemListView()
        {
            systemListView = SystemListView.CreateList(systemListStates, systemListStateNames, (system, world) => SetSystemSelection(system, world, false, true), () => WorldSelection, () => ShowInactiveSystems);
            systemListView.multiColumnHeader.ResizeToFit();
        }

        private void CreateEntityQueryListView()
        {
            entityQueryListView = EntityQueryListView.CreateList(SystemSelection, entityQueryListStates, entityQueryListStateNames, x => SetEntityListSelection(x, false, true), () => SystemSelectionWorld);
        }

        [SerializeField] private bool ShowInactiveSystems;
        [SerializeField] private bool ShowAllWorlds;

        private void CreateWorldPopup()
        {
            m_WorldPopup = new WorldPopup(
                () => WorldSelection,
                x => SetWorldSelection(x, true),
                () => ShowInactiveSystems,
                () =>
                {
                    ShowInactiveSystems = !ShowInactiveSystems;
                    systemListView.Reload();
                },
                () => ShowAllWorlds,
                v => ShowAllWorlds = v);
        }

        private void CreateChunkInfoListView()
        {
            chunkInfoListView = new ChunkInfoListView(chunkInfoListState, SetEntityListChunkFilter);
        }

        private World worldSelection;

        private void OnEnable()
        {
            Instance = this;
            filterUI = new ComponentTypeFilterUI(SetAllEntitiesFilter, () => WorldSelection);

            CreateWorldPopup();
            CreateSystemListView();
            CreateEntityQueryListView();
            CreateEntityListView();
            CreateChunkInfoListView();
            systemListView.TouchSelection();

            showingChunkInfoView = ShowingChunkInfoViewPersistent;
            EditorApplication.playModeStateChanged += OnPlayModeStateChange;
        }

        private void OnDestroy()
        {
            entityListView?.Dispose();
        }

        private void OnDisable()
        {
            entityListView?.Dispose();
            chunkInfoListView?.Dispose();
            if (Instance == this)
                Instance = null;

            ShowingChunkInfoViewPersistent = showingChunkInfoView;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChange;
        }

        private void OnPlayModeStateChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                SetAllEntitiesFilter(null);
            if (change == PlayModeStateChange.ExitingPlayMode && Selection.activeObject is EntitySelectionProxy)
                Selection.activeObject = null;
        }

        private readonly RepaintLimiter repaintLimiter = new RepaintLimiter();

        private void Update()
        {
            systemListView.UpdateTimings();
            if (repaintLimiter.SimulationAdvanced())
            {
                Repaint();
            }
            else if (!Application.isPlaying)
            {
                if (entityListView == null)
                    return;

                if (systemListView == null)
                    return;

                if (systemListView.NeedsReload || entityQueryListView.NeedsReload || entityListView.NeedsReload || !filterUI.TypeListValid())
                    Repaint();
            }
        }

        private void ShowWorldPopup()
        {
            m_WorldPopup.OnGUI(showingPlayerLoop, EditorApplication.isPlaying ? lastPlayModeWorldSelection : lastEditModeWorldSelection, Styles.ToolbarDropdownStyle);
        }

        private string SearchField(string search)
        {
            search = GUILayout.TextField(search, Styles.SearchFieldStyle, GUILayout.Width(Styles.SearchFieldWidth));
            if (!string.IsNullOrEmpty(search))
            {
                if (GUILayout.Button(GUIContent.none, Styles.SearchFieldCancelButton))
                    search = null;
            }
            else
            {
                GUILayout.Box(GUIContent.none, Styles.SearchFieldCancelButtonEmpty);
            }

            return search;
        }

        private void SystemList()
        {
            var rect = GUIHelpers.GetExpandingRect();
            if (World.All.Count > 0)
            {
                systemListView.OnGUI(rect);
            }
            else
            {
                GUIHelpers.ShowCenteredNotification(rect, "No systems (Try pushing Play)");
            }
        }

        private void SystemHeader()
        {
            GUILayout.BeginHorizontal(Styles.ToolbarStyle);
            ShowWorldPopup();
            GUILayout.FlexibleSpace();
            systemListView.searchString = SearchField(systemListView.searchString);
            GUILayout.EndHorizontal();
        }

        public static string FormatBytesForDisplay(long bytes)
        {
            if (bytes < 0)
                return "Unknown";

            if (bytes < 512)
                return String.Format("{0} B", bytes);

            if (bytes < 512 * 1024)
                return String.Format("{0:0.0} KB", bytes / 1024.0);

            bytes /= 1024;
            if (bytes < 512 * 1024)
                return String.Format("{0:0.0} MB", bytes / 1024.0);

            bytes /= 1024;
            return String.Format("{0:0.00} GB", bytes / 1024.0);
        }

        public static string FormatBytesForDisplay(ulong bytes)
        {
            if (bytes < 0)
                return "Unknown";

            if (bytes < 512)
                return String.Format("{0} B", bytes);

            if (bytes < 512 * 1024)
                return String.Format("{0:0.0} KB", bytes / 1024.0);

            bytes /= 1024;
            if (bytes < 512 * 1024)
                return String.Format("{0:0.0} MB", bytes / 1024.0);

            bytes /= 1024;
            return String.Format("{0:0.00} GB", bytes / 1024.0);
        }

        private void EntityHeader()
        {
            GUILayout.BeginHorizontal(Styles.ToolbarStyle);
            if (HasWorld())
            {
                if (SystemSelection == null)
                    GUILayout.Label("All Entities", Styles.ToolbarLabelStyle);
                else
                {
                    var type = SystemSelection.GetSystemType();
                    var typeDisplayName = Properties.Editor.TypeUtility.GetTypeDisplayName(type);
                    if (!string.IsNullOrEmpty(type.Namespace))
                        GUILayout.Label($"{type.Namespace}.{typeDisplayName}", Styles.ToolbarLabelStyle);
                    else
                        GUILayout.Label(typeDisplayName, Styles.ToolbarLabelStyle);
                }
            }
            else
                GUILayout.Label("No World selected", Styles.ToolbarLabelStyle);
            GUILayout.FlexibleSpace();
            long pageSize;
            long chunkReservedPages;
            long chunkCommittedPages;
            ulong chunkReservedBytes;
            ulong chunkCommittedBytes;
            EntityComponentStore.GetChunkMemoryStats(out chunkReservedPages, out chunkCommittedPages, out chunkReservedBytes, out chunkCommittedBytes, out pageSize);
            if(chunkReservedPages > 0)
            {
                GUILayout.Label($"Chunk memory ({FormatBytesForDisplay(pageSize)} per page): Reserved: {chunkReservedPages} pages / {FormatBytesForDisplay(chunkReservedBytes)}, " +
                                $"Committed: {chunkCommittedPages} pages / " +
                                $"{FormatBytesForDisplay(chunkCommittedBytes)}, " +
                                $"In use by selected query: {entityListView.ChunkArray.Length * 16384 / pageSize } pages  / " +
                                $"{FormatBytesForDisplay((ulong)entityListView.ChunkArray.Length * 16384UL)}", Styles.LabelStyle);
            }
            ShowingChunkInfoView = GUILayout.Toggle(ShowingChunkInfoView, "Chunk Info", Styles.ToolbarButtonStyle);
            GUILayout.EndHorizontal();
        }

        private void EntityQueryList()
        {
            if (SystemSelection != null)
            {
                entityQueryListView.SetWidth(CurrentEntityViewWidth);
                var height = Mathf.Min(entityQueryListView.Height + Styles.BoxStyle.padding.vertical, position.height * 0.5f);
                GUILayout.BeginVertical(Styles.BoxStyle, GUILayout.Height(height));

                entityQueryListView.OnGUI(GUIHelpers.GetExpandingRect());
                GUILayout.EndVertical();
            }
            else if (WorldSelection != null)
            {
                GUILayout.BeginVertical();
                filterUI.OnGUI(entityListView.EntityCount);
                GUILayout.EndVertical();
            }
        }

        private EntityListQuery filterQuery;
        private World systemSelectionWorld;

        public void SetAllEntitiesFilter(EntityListQuery entityQuery)
        {
            filterQuery = entityQuery;
            if (WorldSelection == null || SystemSelection != null)
                return;
            ApplyAllEntitiesFilter();
        }

        private void ApplyAllEntitiesFilter()
        {
            SetEntityListSelection(filterQuery, false, true);
        }

        void EntityList()
        {
            if (HasWorld())
            {
                GUILayout.BeginVertical(Styles.BoxStyle);
                entityListView.OnGUI(GUIHelpers.GetExpandingRect());
                GUILayout.EndVertical();
            }
        }

        private void ChunkInfoView()
        {
            GUILayout.BeginVertical(Styles.BoxStyle);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Matching chunks: {entityListView.ChunkArray.Length}", Styles.LabelStyle);
            GUILayout.EndHorizontal();

            chunkInfoListView.OnGUI(GUIHelpers.GetExpandingRect());
            if (chunkInfoListView.HasSelection())
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Clear Selection"))
                {
                    chunkInfoListView.ClearSelection();
                    EditorGUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is EntitySelectionProxy selectionProxy && selectionProxy.Exists)
            {
                SetWorldSelection(selectionProxy.World, false);
                entityListView.SetEntitySelection(selectionProxy.Entity, TreeViewSelectionOptions.RevealAndFrame);
            }
            else
            {
                entityListView.SelectNothing();
            }

            // Ensures we keep the window in sync with selection changes
            Repaint();
        }

        private void OnGUI()
        {
            InitStyles();
            if (Event.current.type == EventType.Layout)
            {
                systemListView.ReloadIfNecessary();
                filterUI.GetTypes();
                entityQueryListView.ReloadIfNecessary();
                entityListView.ReloadIfNecessary();
            }

            if (Selection.activeObject is EntitySelectionProxy selectionProxy && !selectionProxy.Exists)
            {
                Selection.activeObject = null;
                entityListView.SelectNothing();
            }

            {
                GUILayout.BeginArea(new Rect(0f, 0f, kSystemListWidth, position.height)); // begin System side
                SystemHeader();

                GUILayout.BeginVertical(Styles.BoxStyle);
                SystemList();
                GUILayout.EndVertical();

                GUILayout.EndArea(); // end System side
            }

            GUILayout.BeginArea(new Rect(kSystemListWidth, 0, position.width - kSystemListWidth, position.height));
            {
                float toolbarHeight = Styles.ToolbarStyle.fixedHeight;
                GUILayout.BeginArea(new Rect(0, 0, position.width - kSystemListWidth, toolbarHeight));
                EntityHeader();
                GUILayout.EndArea();

                if (HasWorld())
                {
                    // add a slight 1px left and right margin
                    GUILayout.BeginArea(new Rect(0, toolbarHeight, CurrentEntityViewWidth, position.height - toolbarHeight));
                    EntityQueryList();
                    EntityList();
                    GUILayout.EndArea();

                    if (showingChunkInfoView && entityListView.ShowingSomething)
                    {
                        GUILayout.BeginArea(new Rect(CurrentEntityViewWidth, toolbarHeight, kChunkInfoViewWidth, position.height - toolbarHeight));
                        ChunkInfoView();
                        GUILayout.EndArea();
                    }
                }
            }
            GUILayout.EndArea();

            repaintLimiter.RecordRepaint();
        }
    }
}
