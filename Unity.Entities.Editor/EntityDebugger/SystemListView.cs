using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.Profiling;
using System;
using Unity.Collections;

namespace Unity.Entities.Editor
{
    internal unsafe struct SystemSelection : IEquatable<SystemSelection>
    {
        public struct UnmanagedData
        {
            public World World;
            public SystemHandleUntyped Handle;
        }

        public readonly ComponentSystemBase Managed;
        public readonly UnmanagedData Unmanaged;


        public SystemSelection(ComponentSystemBase b)
        {
            Managed = b;
            Unmanaged = default;
        }

        public SystemSelection(SystemHandleUntyped h, World w)
        {
            Managed = null;
            Unmanaged.Handle = h;
            Unmanaged.World = w;
        }

        public SystemState* StatePointer
        {
            get
            {
                if (Managed != null)
                    return Managed.m_StatePtr;
                else if (Unmanaged.World != null)
                    return Unmanaged.World.Unmanaged.ResolveSystemState(Unmanaged.Handle);
                return null;
            }
        }

        public World World
        {
            get
            {
                var ptr = StatePointer;
                if (ptr != null)
                    return ptr->World;
                return null;
            }
        }

        public Type GetSystemType()
        {
            if (Managed != null)
                return Managed.GetType();
            else
                return SystemBaseRegistry.GetStructType(StatePointer->UnmanagedMetaIndex);
        }


        public bool Valid => Managed != null || Unmanaged.World != null;

        public bool Equals(SystemSelection other)
        {
            return ReferenceEquals(Managed, other.Managed) && Unmanaged.Handle == other.Unmanaged.Handle;
        }

        public override int GetHashCode()
        {
            return Managed != null ? Managed.GetHashCode() : Unmanaged.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is SystemSelection sel)
            {
                return Equals(sel);
            }
            else
            {
                return false;
            }
        }

        public static bool operator ==(SystemSelection lhs, SystemSelection rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(SystemSelection lhs, SystemSelection rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static implicit operator SystemSelection(ComponentSystemBase arg) => new SystemSelection(arg);

        public override string ToString()
        {
            return GetSystemType().ToString();
        }
    }

    delegate void SystemSelectionCallback(SystemSelection system, World world);

    class SystemListView : TreeView
    {
        class AverageRecorder
        {
            readonly Recorder recorder;
            int frameCount;
            int totalNanoseconds;
            float lastReading;

            public AverageRecorder(Recorder recorder)
            {
                this.recorder = recorder;
            }

            public void Update()
            {
                ++frameCount;
                totalNanoseconds += (int)recorder.elapsedNanoseconds;
            }

            public float ReadMilliseconds()
            {
                if (frameCount > 0)
                {
                    lastReading = (totalNanoseconds / 1e6f) / frameCount;
                    frameCount = totalNanoseconds = 0;
                }

                return lastReading;
            }
        }
        internal readonly Dictionary<int, SystemSelection> systemsById = new Dictionary<int, SystemSelection>();
        readonly Dictionary<int, World> worldsById = new Dictionary<int, World>();
        readonly Dictionary<SystemSelection, AverageRecorder> recordersBySystem = new Dictionary<SystemSelection, AverageRecorder>();
        readonly Dictionary<int, HideNode> hideNodesById = new Dictionary<int, HideNode>();

        const float kToggleWidth = 22f;
        const float kTimingWidth = 70f;
        const int kAllEntitiesItemId = 0;

        readonly SystemSelectionCallback systemSelectionCallback;
        readonly WorldSelectionGetter getWorldSelection;
        readonly ShowInactiveSystemsGetter getShowInactiveSystems;

        static GUIStyle RightAlignedLabel
        {
            get
            {
                if (rightAlignedText == null)
                {
                    rightAlignedText = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleRight
                    };
                }

                return rightAlignedText;
            }
        }

        static GUIStyle rightAlignedText;

        internal static MultiColumnHeaderState GetHeaderState()
        {
            var columns = new[]
            {
                new MultiColumnHeaderState.Column
                {
                    headerContent = GUIContent.none,
                    contextMenuText = "Enabled",
                    headerTextAlignment = TextAlignment.Left,
                    canSort = false,
                    width = kToggleWidth,
                    minWidth = kToggleWidth,
                    maxWidth = kToggleWidth,
                    autoResize = false,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("Systems"),
                    headerTextAlignment = TextAlignment.Left,
                    sortingArrowAlignment = TextAlignment.Right,
                    canSort = false,
                    sortedAscending = true,
                    width = 100,
                    minWidth = 100,
                    maxWidth = 2000,
                    autoResize = true,
                    allowToggleVisibility = false
                },
                new MultiColumnHeaderState.Column
                {
                    headerContent = new GUIContent("main (ms)"),
                    headerTextAlignment = TextAlignment.Right,
                    canSort = false,
                    width = kTimingWidth,
                    minWidth = kTimingWidth,
                    maxWidth = kTimingWidth,
                    autoResize = false,
                    allowToggleVisibility = false
                }
            };

            return new MultiColumnHeaderState(columns);
        }

        static TreeViewState GetStateForWorld(World world, List<TreeViewState> states, List<string> stateNames)
        {
            if (world == null)
                return new TreeViewState();

            var currentWorldName = world.Name;

            var stateForCurrentWorld = states.Where((t, i) => stateNames[i] == currentWorldName).FirstOrDefault();
            if (stateForCurrentWorld != null)
                return stateForCurrentWorld;

            stateForCurrentWorld = new TreeViewState();
            states.Add(stateForCurrentWorld);
            stateNames.Add(currentWorldName);
            return stateForCurrentWorld;
        }

        public static SystemListView CreateList(List<TreeViewState> states, List<string> stateNames, SystemSelectionCallback systemSelectionCallback, WorldSelectionGetter worldSelectionGetter, ShowInactiveSystemsGetter showInactiveSystemsGetter)
        {
            var state = GetStateForWorld(worldSelectionGetter(), states, stateNames);
            var header = new MultiColumnHeader(GetHeaderState());
            return new SystemListView(state, header, systemSelectionCallback, worldSelectionGetter, showInactiveSystemsGetter);
        }

        internal SystemListView(TreeViewState state, MultiColumnHeader header, SystemSelectionCallback systemSelectionCallback, WorldSelectionGetter worldSelectionGetter, ShowInactiveSystemsGetter showInactiveSystemsGetter) : base(state, header)
        {
            this.getWorldSelection = worldSelectionGetter;
            this.systemSelectionCallback = systemSelectionCallback;
            this.getShowInactiveSystems = showInactiveSystemsGetter;
            columnIndexForTreeFoldouts = 1;
            RebuildNodes();
        }

        HideNode CreateNodeForSystem(int id, SystemSelection sel)
        {
            var active = true;
            var type = sel.GetSystemType();
            if (!typeof(ComponentSystemGroup).IsAssignableFrom(type))
            {
                systemsById.Add(id, sel);
                worldsById.Add(id, sel.World);
                var recorder = Recorder.Get($"{sel.World?.Name ?? "none"} {type.FullName}");
                if (!recordersBySystem.ContainsKey(sel))
                    recordersBySystem.Add(sel, new AverageRecorder(recorder));
                else
                {
                    UnityEngine.Debug.LogError("System added twice: " + sel);
                }
                recorder.enabled = true;
                active = false;
            }

            var typeName = Properties.Editor.TypeUtility.GetTypeDisplayName(type);
            var name = getWorldSelection() == null ? $"{typeName} ({sel.World?.Name ?? "none"})" : typeName;
            var item = new TreeViewItem { id = id, displayName = name };

            var hideNode = new HideNode(item) { Active = active };
            hideNodesById.Add(id, hideNode);
            return hideNode;
        }

        PlayerLoopSystem lastPlayerLoop;

        class HideNode
        {
            public readonly TreeViewItem Item;
            public bool Active = true;
            public List<HideNode> Children;

            public HideNode(TreeViewItem item)
            {
                Item = item;
            }

            public void AddChild(HideNode child)
            {
                if (Children == null)
                    Children = new List<HideNode>();
                Children.Add(child);
            }

            public TreeViewItem BuildList(bool showInactiveSystems)
            {
                if (showInactiveSystems || Active)
                {
                    Item.children = null;
                    if (Children != null)
                    {
                        Item.children = new List<TreeViewItem>();
                        foreach (var child in Children)
                        {
                            var childItem = child.BuildList(showInactiveSystems);
                            if (childItem != null)
                                Item.children.Add(childItem);
                        }
                    }
                    return Item;
                }
                else
                {
                    return null;
                }
            }
        }

        HideNode rootNode;

        void RebuildNodes()
        {
            rootNode = null;
            Reload();
        }

        void AddNodeIgnoreNulls(ref List<HideNode> list, HideNode node)
        {
            if (node == null)
                return;
            if (list == null)
                list = new List<HideNode>();
            list.Add(node);
        }

        HideNode BuildNodesForPlayerLoopSystem(PlayerLoopSystem system, ref int currentId)
        {
            List<HideNode> children = null;
            if (system.subSystemList != null)
            {
                foreach (var subSystem in system.subSystemList)
                {
                    AddNodeIgnoreNulls(ref children, BuildNodesForPlayerLoopSystem(subSystem, ref currentId));
                }
            }
            else
            {
                var executionDelegate = system.updateDelegate;
                ScriptBehaviourUpdateOrder.DummyDelegateWrapper dummy;
                if (executionDelegate != null &&
                    (dummy = executionDelegate.Target as ScriptBehaviourUpdateOrder.DummyDelegateWrapper) != null)
                {
                    var rootSystem = dummy.System;
                    return BuildNodesForComponentSystem(rootSystem, ref currentId);
                }
            }

            if (children != null || getWorldSelection() == null)
            {
                var systemNode = new HideNode(new TreeViewItem() {id = currentId++, displayName = system.type?.Name});
                systemNode.Children = children;
                return systemNode;
            }

            return null;
        }

        HideNode BuildNodesForComponentSystem(SystemSelection systemBase, ref int currentId)
        {
            if (systemBase.Managed != null)
            {
                switch (systemBase.Managed)
                {
                    case ComponentSystemGroup group:
                        List<HideNode> children = null;

                        ref var updateList = ref group.m_MasterUpdateList;

                        for (int i = 0, count = updateList.Length; i < count; ++i)
                        {
                            var updateIndex = updateList[i];

                            if (updateIndex.IsManaged)
                            {
                                var child = group.Systems[updateIndex.Index];
                                AddNodeIgnoreNulls(ref children, BuildNodesForComponentSystem(child, ref currentId));
                            }
                            else
                            {
                                var child = group.UnmanagedSystems[updateIndex.Index];
                                AddNodeIgnoreNulls(ref children, BuildNodesForComponentSystem(new SystemSelection(child, group.World), ref currentId));
                            }
                        }

                        if (children != null || getWorldSelection() == null || getWorldSelection() == group.World)
                        {
                            var groupNode = CreateNodeForSystem(currentId++, group);
                            groupNode.Children = children;
                            return groupNode;
                        }
                        break;
                    case ComponentSystemBase system:
                        {
                            if (getWorldSelection() == null || getWorldSelection() == system.World)
                            {
                                return CreateNodeForSystem(currentId++, system);
                            }
                        }
                        break;
                }
            }
            else
            {
                if (getWorldSelection() == null || getWorldSelection() == systemBase.World)
                {
                    return CreateNodeForSystem(currentId++, systemBase);
                }
            }

            return null;
        }

        void BuildNodeTree()
        {
            systemsById.Clear();
            worldsById.Clear();
            recordersBySystem.Clear();
            hideNodesById.Clear();

            var currentID = kAllEntitiesItemId + 1;

            lastPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            rootNode = BuildNodesForPlayerLoopSystem(lastPlayerLoop, ref currentID)
                ?? new HideNode(new TreeViewItem {id = currentID, displayName = "Root"});

            if (EntityDebugger.extraSystems != null)
            {
                foreach (var system in EntityDebugger.extraSystems)
                {
                    if (system != null)
                        AddNodeIgnoreNulls(ref rootNode.Children, BuildNodesForComponentSystem(system, ref currentID));
                }
            }
        }

        bool GetDefaultExpandedIds(HideNode parent, List<int> ids)
        {
            var shouldExpand = systemsById.ContainsKey(parent.Item.id);
            if (parent.Children != null)
            {
                foreach (var child in parent.Children)
                {
                    shouldExpand |= GetDefaultExpandedIds(child, ids);
                }

                if (shouldExpand)
                {
                    ids.Add(parent.Item.id);
                }
            }

            return shouldExpand;
        }

        protected override TreeViewItem BuildRoot()
        {
            if (rootNode == null)
            {
                BuildNodeTree();
                var expanded = new List<int>();
                GetDefaultExpandedIds(rootNode, expanded);
                expanded.Sort();
                state.expandedIDs = expanded;
            }

            var root = rootNode.BuildList(getShowInactiveSystems());

            if (!root.hasChildren)
                root.children = new List<TreeViewItem>(0);

            if (getWorldSelection() != null)
            {
                root.children.Insert(0, new TreeViewItem(kAllEntitiesItemId, 0, $"All Entities ({getWorldSelection().Name})"));
            }

            root.depth = -1;

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        protected unsafe override void BeforeRowsGUI()
        {
            var becameVisible = false;
            foreach (var idSystemPair in systemsById)
            {
                var selection = idSystemPair.Value;
                var ptr = selection.StatePointer;
                if (ptr != null)
                {
                    var hideNode = hideNodesById[idSystemPair.Key];
                    if (ptr->LastSystemVersion != 0 && !hideNode.Active)
                    {
                        hideNode.Active = true;
                        becameVisible = true;
                    }
                }
            }
            if (becameVisible)
                Reload();
            base.BeforeRowsGUI();
        }

        protected unsafe override void RowGUI(RowGUIArgs args)
        {
            if (args.item.depth == -1)
                return;
            var item = args.item;

            var enabled = GUI.enabled;

            if (systemsById.ContainsKey(item.id))
            {
                var system = systemsById[item.id];
                var ptr = system.StatePointer;
                if (ptr != null)
                {
                    var toggleRect = args.GetCellRect(0);
                    toggleRect.xMin = toggleRect.xMin + 4f;
                    ptr->Enabled = GUI.Toggle(toggleRect, ptr->Enabled, GUIContent.none);

                    var timingRect = args.GetCellRect(2);
                    if (ptr->ShouldRunSystem())
                    {
                        var recorder = recordersBySystem[system];
                        GUI.Label(timingRect, recorder.ReadMilliseconds().ToString("f2"), RightAlignedLabel);
                    }
                    else
                    {
                        GUI.enabled = false;
                        GUI.Label(timingRect, "not run", RightAlignedLabel);
                        GUI.enabled = enabled;
                    }
                }
            }
            else if (args.item.id == kAllEntitiesItemId)
            {
            }
            else
            {
                GUI.enabled = false;
            }

            var indent = GetContentIndent(item);
            var nameRect = args.GetCellRect(1);
            nameRect.xMin = nameRect.xMin + indent;
            GUI.Label(nameRect, item.displayName);
            GUI.enabled = enabled;
        }

        protected override void AfterRowsGUI()
        {
            base.AfterRowsGUI();
            if (Event.current.type == EventType.MouseDown)
            {
                SetSelection(new List<int>());
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 0 && systemsById.ContainsKey(selectedIds[0]))
            {
                systemSelectionCallback(systemsById[selectedIds[0]], worldsById[selectedIds[0]]);
            }
            else
            {
                systemSelectionCallback(null, null);
                SetSelection(getWorldSelection() == null ? new List<int>() : new List<int> {kAllEntitiesItemId});
            }
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void TouchSelection()
        {
            SetSelection(GetSelection(), TreeViewSelectionOptions.FireSelectionChanged);
        }

        bool PlayerLoopsMatch(PlayerLoopSystem a, PlayerLoopSystem b)
        {
            if (a.type != b.type)
                return false;
            if (a.subSystemList == b.subSystemList)
                return true;
            if (a.subSystemList == null || b.subSystemList == null)
                return false;
            if (a.subSystemList.Length != b.subSystemList.Length)
                return false;
            for (var i = 0; i < a.subSystemList.Length; ++i)
            {
                if (!PlayerLoopsMatch(a.subSystemList[i], b.subSystemList[i]))
                    return false;
            }

            return true;
        }

        public bool NeedsReload
        {
            get
            {
                if (!PlayerLoopsMatch(lastPlayerLoop, PlayerLoop.GetCurrentPlayerLoop()))
                    return true;

                foreach (var world in worldsById.Values)
                {
                    if (!world.IsCreated)
                        return true;
                }

                foreach (var selection in systemsById.Values)
                {
                    if (selection.Managed != null)
                    {
                        var system = selection.Managed;
                        if (system.World == null || !system.World.Systems.Contains(system))
                            return true;
                    }
                    else
                    {
                        if (selection.Unmanaged.World != null && !selection.Unmanaged.World.IsCreated)
                            return true;
                    }
                }

                return false;
            }
        }

        public void ReloadIfNecessary()
        {
            if (NeedsReload)
                RebuildNodes();
        }

        int lastTimedFrame;

        public void UpdateTimings()
        {
            if (Time.frameCount == lastTimedFrame)
                return;

            foreach (var recorder in recordersBySystem.Values)
            {
                recorder.Update();
            }

            lastTimedFrame = Time.frameCount;
        }

        public void SetSystemSelection(SystemSelection system)
        {
            foreach (var pair in systemsById)
            {
                if (pair.Value == system)
                {
                    SetSelection(new List<int> {pair.Key});
                    return;
                }
            }
            SetSelection(new List<int>());
        }
    }
}
