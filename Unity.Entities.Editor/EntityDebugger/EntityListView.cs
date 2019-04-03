using UnityEditor.IMGUI.Controls;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.Entities.Editor
{
    
    public delegate void EntitySelectionCallback(Entity selection, bool updateList);
    public delegate World WorldSelectionGetter();
    
    public class EntityListView : TreeView {
        private readonly Dictionary<int, Entity> entitiesById = new Dictionary<int, Entity>();

        public ComponentGroup SelectedComponentGroup
        {
            get { return selectedComponentGroup; }
            set
            {
                if (selectedComponentGroup != value)
                {
                    selectedComponentGroup = value;
                    Reload();
                }
            }
        }
        private ComponentGroup selectedComponentGroup;
        int                    cachedVersion;

        private EntitySelectionCallback setEntitySelection;
        private WorldSelectionGetter getWorldSelection;

        public EntityListView(TreeViewState state, ComponentGroup componentGroup, EntitySelectionCallback entitySelectionCallback, WorldSelectionGetter getWorldSelection) : base(state)
        {
            this.setEntitySelection = entitySelectionCallback;
            this.getWorldSelection = getWorldSelection;
            SelectedComponentGroup = componentGroup;
            Reload();
        }

        public void UpdateIfNecessary()
        {
            if (getWorldSelection() == null)
                return;
            if (selectedComponentGroup == null)
            {
                if (getWorldSelection().GetExistingManager<EntityManager>().Version != cachedVersion)
                    Reload();
            }
            else if (selectedComponentGroup.GetCombinedComponentOrderVersion() != cachedVersion)
                Reload();
        }

        private TreeViewItem CreateEntityItem(Entity entity)
        {
            entitiesById.Add(entity.Index, entity);
            return new TreeViewItem { id = entity.Index };
        }

        protected override TreeViewItem BuildRoot()
        {
            entitiesById.Clear();
            var managerId = -1;
            var root  = new TreeViewItem { id = managerId--, depth = -1, displayName = "Root" };
            if (getWorldSelection() == null)
            {
                root.AddChild(new TreeViewItem { id = managerId, displayName = "No world selected"});
                cachedVersion = -1;
            }
            else
            {
                if (SelectedComponentGroup == null)
                {
                    var entityManager = getWorldSelection().GetExistingManager<EntityManager>();
                    var array = entityManager.GetAllEntities(Allocator.Temp);
                    for (var i = 0; i < array.Length; ++i)
                        root.AddChild(CreateEntityItem(array[i]));
                    array.Dispose();
                    cachedVersion = entityManager.Version;
                }
                else
                {
                    getWorldSelection().GetExistingManager<EntityManager>().CompleteAllJobs();
                    var entityArray = SelectedComponentGroup.GetEntityArray();
                    for (var i = 0; i < entityArray.Length; ++i)
                        root.AddChild(CreateEntityItem(entityArray[i]));
                    cachedVersion = SelectedComponentGroup.GetCombinedComponentOrderVersion();
                }

                if (entitiesById.Count == 0)
                {
                    root.AddChild(new TreeViewItem { id = managerId, displayName = "No Entities"});
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

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item.displayName == null)
                args.label = args.item.displayName = $"Entity {entitiesById[args.item.id].Index.ToString()}";
            base.RowGUI(args);
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 0)
            {
                if (entitiesById.ContainsKey(selectedIds[0]))
                    setEntitySelection(entitiesById[selectedIds[0]], false);
            }
            else
            {
                setEntitySelection(Entity.Null, false);
            }
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        public void SelectNothing()
        {
            SetSelection(new List<int>());
        }

        public void SetEntitySelection(Entity entitySelection)
        {
            if (entitySelection != Entity.Null && getWorldSelection().GetExistingManager<EntityManager>().Exists(entitySelection))
                SetSelection(new List<int>{entitySelection.Index});
        }

        public void TouchSelection()
        {
            SetSelection(GetSelection(), TreeViewSelectionOptions.FireSelectionChanged);
        }

        public void FrameSelection()
        {
            var selection = GetSelection();
            if (selection.Count > 0)
            {
                FrameItem(selection[0]);
            }
        }
    }
}
