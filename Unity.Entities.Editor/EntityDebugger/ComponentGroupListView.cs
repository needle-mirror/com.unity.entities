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
        private readonly Dictionary<int, string[]> componentGroupTypeNamesById = new Dictionary<int, string[]>();
        private readonly Dictionary<int, ComponentType.AccessMode[]> componentGroupTypeModesById = new Dictionary<int, ComponentType.AccessMode[]>();

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
            Reload();
        }

        public float Height => Mathf.Max(selectedSystem?.ComponentGroups?.Length ?? 0, 1)*rowHeight;

        internal static int CompareTypes(ComponentType x, ComponentType y)
        {
            var accessModeOrder = SortOrderFromAccessMode(x.AccessModeType).CompareTo(SortOrderFromAccessMode(y.AccessModeType));
            return accessModeOrder != 0 ? accessModeOrder : string.Compare(x.GetType().Name, y.GetType().Name, StringComparison.InvariantCulture);
        }

        private static int SortOrderFromAccessMode(ComponentType.AccessMode mode)
        {
            switch (mode)
            {
                    case ComponentType.AccessMode.ReadOnly:
                        return 0;
                    case ComponentType.AccessMode.ReadWrite:
                        return 1;
                    case ComponentType.AccessMode.Subtractive:
                        return 2;
                    default:
                        throw new ArgumentException("Unrecognized AccessMode");
            }
        }

        protected override TreeViewItem BuildRoot()
        {
            componentGroupsById.Clear();
            componentGroupTypeModesById.Clear();
            componentGroupTypeNamesById.Clear();
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
                    var types = new List<ComponentType>(group.Types.Skip(1)); // Skip Entity
                    types.Sort(CompareTypes);
                    componentGroupTypeNamesById.Add(currentId, (from t in types select t.GetManagedType().Name).ToArray());
                    componentGroupTypeModesById.Add(currentId, (from t in types select t.AccessModeType).ToArray());

                    var groupItem = new TreeViewItem { id = currentId++ };
                    root.AddChild(groupItem);
                }
                SetupDepthsFromParentsAndChildren(root);
            }
            return root;
        }

        public override void OnGUI(Rect rect)
        {
            if (getWorldSelection()?.GetExistingManager<EntityManager>()?.IsCreated == true)
                base.OnGUI(rect);
        }

        private void DrawTypeAndMovePosition(ref Vector2 position, string name, ComponentType.AccessMode mode)
        {
            var style = StyleForAccessMode(mode);
            var content = new GUIContent(name);
            var labelRect = new Rect(position, style.CalcSize(content));
            GUI.Label(labelRect, content, style);
            position.x += labelRect.width + 2f;
        }

        internal static GUIStyle StyleForAccessMode(ComponentType.AccessMode mode)
        {
            switch (mode)
            {
                case ComponentType.AccessMode.ReadOnly:
                    return EntityDebuggerStyles.ComponentReadOnly;
                case ComponentType.AccessMode.ReadWrite:
                    return EntityDebuggerStyles.ComponentReadWrite;
                case ComponentType.AccessMode.Subtractive:
                    return EntityDebuggerStyles.ComponentSubtractive;
                default:
                    throw new ArgumentException("Unrecognized access mode");
            }
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            base.RowGUI(args);
            if (!componentGroupsById.ContainsKey(args.item.id))
                return;

            var componentGroup = componentGroupsById[args.item.id];
            var names = componentGroupTypeNamesById[args.item.id];
            var modes = componentGroupTypeModesById[args.item.id];

            var position = args.rowRect.position;
            position.x = GetContentIndent(args.item);
            position.y += 1;
            
            for (var i = 0; i < names.Length; ++i)
            {
                DrawTypeAndMovePosition(ref position, names[i], modes[i]);
            }
            
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
