using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEditor.IMGUI.Controls;

namespace Unity.Entities.Editor
{
    public class EntityDebugger : EditorWindow
    {
        private const float kSystemListWidth = 350f;

#if UNITY_2018_2_OR_NEWER
        [MenuItem("Window/Debug/Entity Debugger", false)]
#else
        [MenuItem("Window/Entity Debugger", false, 2017)]
#endif
        static void OpenWindow()
        {
            GetWindow<EntityDebugger>("Entity Debugger");
        }

        private static GUIStyle Box
        {
            get
            {
                if (box == null)
                {
                    box = new GUIStyle(GUI.skin.box)
                    {
                        margin = new RectOffset(),
                        padding = new RectOffset(1, 0, 1, 0),
                        overflow = new RectOffset(0, 1, 0, 1)
                    };
                }

                return box;
            }
        }

        private static GUIStyle box;

        public ScriptBehaviourManager SystemSelection
        {
            get { return systemSelection; }
        }

        public void SetSystemSelection(ScriptBehaviourManager manager, bool updateList, bool propagate)
        {
            systemSelection = manager;
            if (updateList)
                systemListView.SetSystemSelection(manager);
            CreateComponentGroupListView();
            if (propagate)
            {
                if (systemSelection is ComponentSystemBase)
                    componentGroupListView.TouchSelection();
                else
                    ApplyAllEntitiesFilter();
            }
        }

        private ScriptBehaviourManager systemSelection;

        public ComponentGroup ComponentGroupSelection
        {
            get { return componentGroupSelection; }
        }

        public void SetComponentGroupSelection(ComponentGroup newSelection, bool updateList, bool propagate)
        {
            componentGroupSelection = newSelection;
            if (updateList)
                componentGroupListView.SetComponentGroupSelection(newSelection);
            entityListView.SelectedComponentGroup = newSelection;
            if (propagate)
                entityListView.TouchSelection();
        }

        private ComponentGroup componentGroupSelection;
        
        public Entity EntitySelection
        {
            get { return selectionProxy.Entity; }
        }

        public void SetEntitySelection(Entity newSelection, bool updateList)
        {
            if (updateList)
                entityListView.SetEntitySelection(newSelection);
            if (WorldSelection != null && newSelection != Entity.Null)
            {
                selectionProxy.SetEntity(WorldSelection, newSelection);
                Selection.activeObject = selectionProxy;
            }
            else if (Selection.activeObject == selectionProxy)
            {
                Selection.activeObject = null;
            }
        }

        public static void SetAllSelections(World world, ComponentSystemBase system, ComponentGroup componentGroup, Entity entity)
        {
            if (Instance == null)
                return;
            Instance.SetWorldSelection(world, false);
            Instance.SetSystemSelection(system, true, false);
            Instance.SetComponentGroupSelection(componentGroup, true, false);
            Instance.SetEntitySelection(entity, true);
            Instance.entityListView.FrameSelection();
        }

        public static EntityDebugger Instance { get; set; }

        private EntitySelectionProxy selectionProxy;
        
        [SerializeField] private List<TreeViewState> componentGroupListStates = new List<TreeViewState>();
        [SerializeField] private List<string> componentGroupListStateNames = new List<string>();
        private ComponentGroupListView componentGroupListView;
        
        [SerializeField] private List<TreeViewState> systemListStates = new List<TreeViewState>();
        [SerializeField] private List<string> systemListStateNames = new List<string>();
        private SystemListView systemListView;

        [SerializeField] private TreeViewState entityListState = new TreeViewState();
        private EntityListView entityListView;

        private ComponentTypeFilterUI filterUI;
        
        private string[] worldNames => (from x in World.AllWorlds select x.Name).ToArray();

        private void SelectWorldByName(string name, bool propagate)
        {
            foreach (var world in World.AllWorlds)
            {
                if (world.Name == name)
                {
                    SetWorldSelection(world, propagate);
                    return;
                }
            }

            SetWorldSelection(null, propagate);
        }
        
        public World WorldSelection
        {
            get
            {
                if (worldSelection != null && worldSelection.IsCreated)
                    return worldSelection;
                return null;
            }
        }

        public void SetWorldSelection(World selection, bool propagate)
        {
            if (worldSelection != selection)
            {
                worldSelection = selection;
                if (worldSelection != null)
                {
                    lastSelectedWorldName = worldSelection.Name;
                }
                    
                CreateSystemListView();
                systemListView.multiColumnHeader.ResizeToFit();
                if (propagate)
                    systemListView.TouchSelection();
            }
        }

        private void CreateEntityListView()
        {
            entityListView = new EntityListView(entityListState, ComponentGroupSelection, SetEntitySelection, () => WorldSelection);
        }

        private void CreateSystemListView()
        {
            systemListView = SystemListView.CreateList(systemListStates, systemListStateNames, SetSystemSelection, () => WorldSelection);
        }

        private void CreateComponentGroupListView()
        {
            componentGroupListView = ComponentGroupListView.CreateList(SystemSelection as ComponentSystemBase, componentGroupListStates, componentGroupListStateNames, SetComponentGroupSelection, () => WorldSelection);
        }

        private World worldSelection;
        [SerializeField] private string lastSelectedWorldName;

        private int selectedWorldIndex
        {
            get { return World.AllWorlds.IndexOf(WorldSelection); }
            set
            {
                if (value >= 0 && value < World.AllWorlds.Count)
                    SetWorldSelection(World.AllWorlds[value], true);
            }
        }

        private readonly string[] noWorldsName = new[] {"No worlds"};

        void OnEnable()
        {
            Instance = this;
            selectionProxy = ScriptableObject.CreateInstance<EntitySelectionProxy>();
            selectionProxy.hideFlags = HideFlags.HideAndDontSave;
            filterUI = new ComponentTypeFilterUI(SetAllEntitiesFilter, () => WorldSelection);
            CreateSystemListView();
            CreateComponentGroupListView();
            CreateEntityListView();
            systemListView.TouchSelection();
            EditorApplication.playModeStateChanged += OnPlayModeStateChange;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
            if (selectionProxy)
                DestroyImmediate(selectionProxy);
            
            EditorApplication.playModeStateChanged -= OnPlayModeStateChange;
        }

        void OnPlayModeStateChange(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                SetAllEntitiesFilter(null);
            if (change == PlayModeStateChange.ExitingPlayMode && Selection.activeObject == selectionProxy)
                Selection.activeObject = null;
        }
        
        private float lastUpdate;
        
        void Update()
        {
            systemListView.UpdateTimings();
            
            systemListView.UpdateIfNecessary();
            componentGroupListView.UpdateIfNecessary();
            entityListView.UpdateIfNecessary();
            filterUI.GetTypes();
            
            if (Time.realtimeSinceStartup > lastUpdate + 0.5f) 
            { 
                Repaint(); 
            }
        } 

        void WorldPopup()
        {
            if (World.AllWorlds.Count == 0)
            {
                var guiEnabled = GUI.enabled;
                GUI.enabled = false;
                EditorGUILayout.Popup(0, noWorldsName);
                GUI.enabled = guiEnabled;
            }
            else
            {
                if (WorldSelection == null || !WorldSelection.IsCreated)
                {
                    SelectWorldByName(lastSelectedWorldName, true);
                    if (WorldSelection == null)
                    {
                        SetWorldSelection(World.AllWorlds[0], true);
                    }
                }
                selectedWorldIndex = EditorGUILayout.Popup(selectedWorldIndex, worldNames);
            }
        }

        void SystemList()
        {
            var rect = GUIHelpers.GetExpandingRect();
            if (World.AllWorlds.Count != 0)
            {
                systemListView.OnGUI(rect);
            }
            else
            {
                GUIHelpers.ShowCenteredNotification(rect, "No systems (Try pushing Play)");
            }
        }

        void SystemHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Systems", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            AlignHeader(WorldPopup);
            GUILayout.EndHorizontal();
        }

        void EntityHeader()
        {
            if (WorldSelection == null)
                return;
            GUILayout.BeginHorizontal();
            if (SystemSelection == null)
            {
                GUILayout.Label("All Entities", EditorStyles.boldLabel);
            }
            else
            {
                var type = SystemSelection.GetType();
                AlignHeader(() => GUILayout.Label(type.Namespace, EditorStyles.label));
                GUILayout.Label(type.Name, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                var system = SystemSelection as ComponentSystemBase;
                if (system != null)
                {
                    var running = system.Enabled && system.ShouldRunSystem();
                    AlignHeader(() => GUILayout.Label($"running: {(running ? "yes" : "no")}"));
                }
            }
            GUILayout.EndHorizontal();
        }

        void ComponentGroupList()
        {
            if (SystemSelection is ComponentSystemBase)
            {
                GUILayout.BeginVertical(Box, GUILayout.Height(componentGroupListView.Height + Box.padding.bottom + Box.padding.top));

                componentGroupListView.OnGUI(GUIHelpers.GetExpandingRect());
                GUILayout.EndVertical();
            }
            else if (WorldSelection != null)
            {
                filterUI.OnGUI();
            }
        }

        private ComponentGroup filterGroup;

        public void SetAllEntitiesFilter(ComponentGroup componentGroup)
        {
            filterGroup = componentGroup;
            if (WorldSelection == null || SystemSelection is ComponentSystemBase)
                return;
            ApplyAllEntitiesFilter();
        }
        
        private void ApplyAllEntitiesFilter()
        {
            SetComponentGroupSelection(filterGroup, false, true);
        }

        void EntityList()
        {
            var showingAllEntities = !(SystemSelection is ComponentSystemBase);
            var componentGroupHasEntities = ComponentGroupSelection != null && !ComponentGroupSelection.IsEmptyIgnoreFilter;
            var somethingToShow = showingAllEntities || componentGroupHasEntities;
            if (WorldSelection == null || !somethingToShow)
                return;
            GUILayout.BeginVertical(Box);
            entityListView.OnGUI(GUIHelpers.GetExpandingRect());
            GUILayout.EndVertical();
        }

        void AlignHeader(System.Action header)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(6f);
            header();
            GUILayout.EndVertical();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject != selectionProxy)
            {
                entityListView.SelectNothing();
            }
        }

        void OnGUI()
        {
            if (Selection.activeObject == selectionProxy)
            {
                if (!selectionProxy.Exists)
                {
                    Selection.activeObject = null;
                    entityListView.SelectNothing();
                }
            }

            GUILayout.BeginHorizontal();
            
            GUILayout.BeginVertical(GUILayout.Width(kSystemListWidth)); // begin System side
            SystemHeader();
            
            GUILayout.BeginVertical(Box);
            SystemList();
            GUILayout.EndVertical();
            
            GUILayout.EndVertical(); // end System side
            
            GUILayout.BeginVertical(GUILayout.Width(position.width - kSystemListWidth)); // begin Entity side

            EntityHeader();
            ComponentGroupList();
            EntityList();
            
            GUILayout.EndVertical(); // end Entity side
            
            GUILayout.EndHorizontal();

            lastUpdate = Time.realtimeSinceStartup;
        }
    }
}