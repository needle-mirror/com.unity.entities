using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using HierarchyModel = Unity.Entities.Editor.Hierarchy;

namespace Unity.Entities.Editor
{
    /// <summary>
    /// The <see cref="HierarchyElement"/> handles all rendering for the hierarchy. It is responsible for managing the list view and loading states.
    /// </summary>
    class HierarchyElement : VisualElement
    {
        /// <summary>
        /// Nested element to handle the initial loading screen.
        /// </summary>
        class HierarchyLoadingElement : VisualElement
        {
            readonly ProgressBar m_ProgressBar;
            readonly SpinnerElement m_SpinnerElement;
            
            public HierarchyLoadingElement()
            {
                Resources.Templates.Hierarchy.Loading.Clone(this);
                AddToClassList(UssClasses.Hierarchy.Loading);
                m_ProgressBar = this.Q<ProgressBar>();
                m_SpinnerElement = this.Q<SpinnerElement>();
            }

            public void SetProgress(float progress)
            {
                m_ProgressBar.value = progress * 100;
            }

            public void SetVisibility(bool isVisible)
            {
                VisualElementExtensions.SetVisibility(this, isVisible);

                if (isVisible)
                    m_SpinnerElement.Resume();
                else
                    m_SpinnerElement.Pause();
            }
        }
        
        enum ViewType
        {
            None,
            Hidden,
            Loading,
            Hierarchy,
            Message
        }
        
        const string k_ListViewName = "unity-tree-view__list-view";

        static readonly string k_ComponentTypeNotFoundTitle = L10n.Tr("Type not found");
        static readonly string k_ComponentTypeNotFoundContent = L10n.Tr("\"{0}\" is not a component type");
        static readonly string k_NoEntitiesFoundTitle = L10n.Tr("No entity matches your search");
        
        ViewType m_ViewType;

        readonly HierarchyModel m_Model;
        readonly HierarchyNodes m_Nodes;
        
        readonly HierarchyListView m_HierarchyListView;
        
        readonly HierarchyLoadingElement m_LoadingElement;
        readonly CenteredMessageElement m_MessageElement;
        
        /// <summary>
        /// The last version the list view refreshed at.
        /// </summary>
        int m_ViewChangeVersion;

        public event Action<HierarchyNodeHandle> OnSelectionChanged;

        public HierarchyElement(HierarchyModel model)
        {
            Resources.Templates.DotsEditorCommon.AddStyles(this);
            Resources.Templates.Hierarchy.Root.AddStyles(this);
            
            AddToClassList(UssClasses.Hierarchy.Root);

            m_Model = model;
            m_Nodes = m_Model.GetNodes();
            
            m_HierarchyListView = new HierarchyListView(m_Model)
            {
                name = k_ListViewName,
                OnMakeItem = OnMakeItem,
                SelectionType = SelectionType.Single,
            };

            m_HierarchyListView.AddToClassList(k_ListViewName);
            m_HierarchyListView.OnSelectedHandlesChanged += HandleListViewSelectedHandlesChanged;
            m_HierarchyListView.RegisterCallback<KeyDownEvent>(OnKeyDown);

            m_LoadingElement = new HierarchyLoadingElement();
            m_MessageElement = new CenteredMessageElement();
            
            hierarchy.Add(m_HierarchyListView);
            hierarchy.Add(m_LoadingElement);
            hierarchy.Add(m_MessageElement);
            
            m_HierarchyListView.SetVisibility(false);
            m_LoadingElement.SetVisibility(false);
            m_MessageElement.SetVisibility(false);
            
            // @FIXME Switch to pointer down event. Currently there is an issue when interacting with the toggle.
            m_HierarchyListView.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == (int) MouseButton.LeftMouse)
                {
                    // Clear the global selection.
                    Selection.activeObject = null;
                }
            });
            
            m_HierarchyListView.RegisterCallback<MouseUpEvent>(evt =>
            {
                m_HierarchyListView.contentContainer.Focus();
            }, TrickleDown.TrickleDown);

            Refresh(); 
        }

        void SetView(ViewType viewType)
        {
            if (m_ViewType == viewType)
                return;

            m_ViewType = viewType;
            
            switch (m_ViewType)
            {
                case ViewType.Hidden:
                    m_HierarchyListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Loading:
                    m_HierarchyListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(true);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Hierarchy:
                    m_HierarchyListView.SetVisibility(true);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(false);
                    break;
                case ViewType.Message:
                    m_HierarchyListView.SetVisibility(false);
                    m_LoadingElement.SetVisibility(false);
                    m_MessageElement.SetVisibility(true);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(viewType), viewType, null);
            }
        }

        public void SetSelection(HierarchyNodeHandle handle)
        {
            // Force this node to be visible. This may trigger a rebuild of the expanded nodes model.
            m_Nodes.SetAncestorsExpanded(handle);
            m_HierarchyListView.SetSelectionWithoutNotify(handle);
        }

        public void ClearSelection()
        {
            m_HierarchyListView.ClearSelection();
        }
        
        public void Refresh()
        {
            if (m_Nodes.ChangeVersion == 0)
            {
                m_LoadingElement.SetProgress(m_Model.GetEstimatedProgress());
                SetView(ViewType.Loading);
                return;
            }

            if (m_Nodes.HasFilter())
            {
                var filter = m_Nodes.GetFilter();
                
                if (!filter.IsValid && !string.IsNullOrEmpty(filter.ErrorComponentType))
                {
                    m_MessageElement.Title = k_ComponentTypeNotFoundTitle;
                    m_MessageElement.Message = string.Format(k_ComponentTypeNotFoundContent, filter.ErrorComponentType);
                    SetView(ViewType.Message);
                    return;
                }

                if (m_Nodes.Count == 0)
                {
                    m_MessageElement.Title = k_NoEntitiesFoundTitle;
                    m_MessageElement.Message = string.Empty;
                    SetView(ViewType.Message);
                    return;
                }
            }
            
            SetView(ViewType.Hierarchy);
            
            if (m_Nodes.IsChanged(m_ViewChangeVersion))
            {
                m_ViewChangeVersion = m_Nodes.ChangeVersion;
                m_HierarchyListView.Refresh();
            }
        }
        
        void HandleListViewSelectedHandlesChanged(IEnumerable<HierarchyNodeHandle> selected)
        {
            using var enumerator = selected.GetEnumerator();
            
            var selection = enumerator.MoveNext() 
                ? m_Nodes[enumerator.Current].GetHandle() 
                : default;

            OnSelectionChanged?.Invoke(selection);
        }

        /// <summary>
        /// Invoked by the <see cref="UnityEngine.UIElements.ListView"/> whenever a new virtualized item is created.
        /// </summary>
        void OnMakeItem(HierarchyListViewItem element)
        {
            element.Q<Toggle>().RegisterCallback<MouseUpEvent>(OnToggleExpandedState);
        }
        
        void OnToggleExpandedState(MouseUpEvent evt)
        {
            var toggle = evt.target as VisualElement;
            var element = toggle.GetFirstAncestorOfType<HierarchyListViewItem>();
            var handle = element.Handle;

            SetExpandedState(handle, (evt.modifiers & EventModifiers.Alt) != 0, null);

            evt.StopPropagation();
            
            // Make sure our TreeView gets focus.
            m_HierarchyListView.contentContainer.Focus();
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            var shouldStopPropagation = true;
            var currentNode = m_HierarchyListView.SelectedHandle;

            switch (evt.keyCode)
            {
                case KeyCode.RightArrow:
                    SetExpandedState(currentNode, (evt.modifiers & EventModifiers.Alt) != 0, true);
                    break;
                case KeyCode.LeftArrow:
                    SetExpandedState(currentNode, (evt.modifiers & EventModifiers.Alt) != 0, false);
                    break;
                default:
                    shouldStopPropagation = false;
                    break;
            }

            if (shouldStopPropagation)
                evt.StopPropagation();
        }

        void SetExpandedState(HierarchyNodeHandle handle, bool recursive, bool? isExpanded)
        {
            // Flip the expanded state for the underlying nodes.
            if (m_Nodes[handle].GetChildCount() > 0)
            {
                isExpanded ??= !m_Nodes.IsExpanded(handle);

                if (recursive)
                    SetExpandedRecursive(handle, isExpanded.Value, m_Nodes);
                else
                    m_Nodes.SetExpanded(handle, isExpanded.Value);

                Refresh();
            }

            static void SetExpandedRecursive(HierarchyNodeHandle handle, bool isExpanded, HierarchyNodes nodes)
            {
                nodes.SetExpanded(handle, isExpanded);

                if (nodes[handle].GetChildCount() == 0)
                    return;

                foreach (var children in nodes[handle])
                    SetExpandedRecursive(children.GetHandle(), isExpanded, nodes);
            }
        }

        public void Reset()
        {
            m_ViewChangeVersion = 0;
            Refresh();
        }
    }
}