using System;
using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.Entities.Editor
{
    
    public delegate void SetComponentGroupSelection(ComponentGroup group, bool updateList, bool propagate);
    
    public class ComponentGroupListView : TreeView {
        private readonly Dictionary<int, ComponentGroup> componentGroupsById = new Dictionary<int, ComponentGroup>();
        private readonly Dictionary<int, List<GUIStyle>> componentGroupStylesById = new Dictionary<int, List<GUIStyle>>();
        private readonly Dictionary<int, List<GUIContent>> componentGroupNamesById = new Dictionary<int, List<GUIContent>>();
        private readonly Dictionary<int, List<Rect>> componentGroupRectsById = new Dictionary<int, List<Rect>>();
        private readonly Dictionary<int, float> componentGroupHeightsById = new Dictionary<int, float>();

        public ComponentSystemBase SelectedSystem
        {
            get { return selectedSystem; }
            set
            {
                if (selectedSystem != value)
                {
                    selectedSystem = value;
                    Reload();
                }
            }
        }
        private ComponentSystemBase selectedSystem;

        private readonly WorldSelectionGetter getWorldSelection;
        private readonly SetComponentGroupSelection componentGroupSelectionCallback;

        private static TreeViewState GetStateForSystem(ComponentSystemBase system, List<TreeViewState> states, List<string> stateNames)
        {
            if (system == null)
                return new TreeViewState();
            
            var currentSystemName = system.GetType().FullName;

            var stateForCurrentSystem = states.Where((t, i) => stateNames[i] == currentSystemName).FirstOrDefault();
            if (stateForCurrentSystem != null)
                return stateForCurrentSystem;
            
            stateForCurrentSystem = new TreeViewState();
            if (system.ComponentGroups != null && system.ComponentGroups.Length > 0)
                stateForCurrentSystem.expandedIDs = new List<int> {1};
            states.Add(stateForCurrentSystem);
            stateNames.Add(currentSystemName);
            return stateForCurrentSystem;
        }

        public static ComponentGroupListView CreateList(ComponentSystemBase system, List<TreeViewState> states, List<string> stateNames,
            SetComponentGroupSelection componentGroupSelectionCallback, WorldSelectionGetter worldSelectionGetter)
        {
            var state = GetStateForSystem(system, states, stateNames);
            return new ComponentGroupListView(state, system, componentGroupSelectionCallback, worldSelectionGetter);
        }

        public ComponentGroupListView(TreeViewState state, ComponentSystemBase system, SetComponentGroupSelection componentGroupSelectionCallback, WorldSelectionGetter worldSelectionGetter) : base(state)
        {
            this.getWorldSelection = worldSelectionGetter;
            this.componentGroupSelectionCallback = componentGroupSelectionCallback;
            selectedSystem = system;
            rowHeight += 1;
            showAlternatingRowBackgrounds = true;
            Reload();
        }

        public float Height => Mathf.Max(selectedSystem?.ComponentGroups?.Length ?? 0, 1)*rowHeight;

        protected override float GetCustomRowHeight(int row, TreeViewItem item)
        {
            return componentGroupHeightsById.ContainsKey(item.id) ? componentGroupHeightsById[item.id] + 2 : rowHeight;
        }

        protected override TreeViewItem BuildRoot()
        {
            componentGroupsById.Clear();
            componentGroupHeightsById.Clear();
            var currentId = 0;
            var root  = new TreeViewItem { id = currentId++, depth = -1, displayName = "Root" };
            if (getWorldSelection() == null)
            {
                root.AddChild(new TreeViewItem { id = currentId, displayName = "No world selected"});
            }
            else if (SelectedSystem == null)
            {
                root.AddChild(new TreeViewItem { id = currentId, displayName = "Null System"});
            }
            else if (SelectedSystem.ComponentGroups == null || SelectedSystem.ComponentGroups.Length == 0)
            {
                root.AddChild(new TreeViewItem { id = currentId, displayName = "No Component Groups in Manager"});
            }
            else
            {
                foreach (var group in SelectedSystem.ComponentGroups)
                {
                    componentGroupsById.Add(currentId, group);

                    var groupItem = new TreeViewItem { id = currentId++ };
                    root.AddChild(groupItem);
                }
                SetupDepthsFromParentsAndChildren(root);
            }
            return root;
        }

        private float width;

        private void CalculateDrawingParts(float newWidth)
        {
            width = newWidth;
            componentGroupStylesById.Clear();
            componentGroupNamesById.Clear();
            componentGroupRectsById.Clear();
            componentGroupHeightsById.Clear();
            foreach (var idGroupPair in componentGroupsById)
            {
                ComponentGroupGUI.CalculateDrawingParts(idGroupPair.Value.Types, width, out var height, out var styles, out var names, out var rects);
                componentGroupStylesById.Add(idGroupPair.Key, styles);
                componentGroupNamesById.Add(idGroupPair.Key, names);
                componentGroupRectsById.Add(idGroupPair.Key, rects);
                componentGroupHeightsById.Add(idGroupPair.Key, height);
            }
            RefreshCustomRowHeights();
        }

        public override void OnGUI(Rect rect)
        {

            if (getWorldSelection()?.GetExistingManager<EntityManager>()?.IsCreated == true)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    CalculateDrawingParts(rect.width - 60f);
                }
                base.OnGUI(rect);
            }
        }

        protected override void BeforeRowsGUI()
        {
            base.BeforeRowsGUI();
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            base.RowGUI(args);
            if (!componentGroupsById.ContainsKey(args.item.id) || Event.current.type != EventType.Repaint)
                return;

            var componentGroup = componentGroupsById[args.item.id];

            var position = args.rowRect.position;
            position.x = GetContentIndent(args.item);
            position.y += 1;
            
            ComponentGroupGUI.DrawComponentList(
                new Rect(position.x, position.y, componentGroupHeightsById[args.item.id], width),
                componentGroupStylesById[args.item.id],
                componentGroupNamesById[args.item.id],
                componentGroupRectsById[args.item.id]);
            
            var countString = componentGroup.CalculateLength().ToString();
            DefaultGUI.LabelRightAligned(args.rowRect, countString, args.selected, args.focused);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 0 && componentGroupsById.ContainsKey(selectedIds[0]))
            {
                componentGroupSelectionCallback(componentGroupsById[selectedIds[0]], false, true);
            }
            else
            {
                componentGroupSelectionCallback(null, false, true);
            }
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void SetComponentGroupSelection(ComponentGroup group)
        {
            foreach (var pair in componentGroupsById)
            {
                if (pair.Value == group)
                {
                    SetSelection(new List<int> {pair.Key});
                    return;
                }
            }
            SetSelection(new List<int>());
        }

        public void TouchSelection()
        {
            SetSelection(GetSelection(), TreeViewSelectionOptions.FireSelectionChanged);
        }

        public void UpdateIfNecessary()
        {
            var expectedGroupCount = SelectedSystem?.ComponentGroups?.Length ?? 0; 
            if (expectedGroupCount != componentGroupsById.Count)
                Reload();
        }
    }
}
