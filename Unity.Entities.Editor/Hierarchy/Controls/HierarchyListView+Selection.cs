using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    partial class HierarchyListView
    {
        static readonly HierarchyNodeHandle[] k_SingleSelectionBuffer = new HierarchyNodeHandle[1];
        static readonly List<HierarchyNodeHandle> k_RangeSelectionBuffer = new List<HierarchyNodeHandle>();

        SelectionType m_SelectionType;
        
        bool m_IsRangeSelectionDirectionUp;
        
        /// <summary>
        /// Serialized set of selected nodes.
        /// </summary>
        [SerializeField] readonly List<HierarchyNodeHandle> m_SelectedHandles = new List<HierarchyNodeHandle>();

        public HierarchyNodeHandle SelectedHandle => m_SelectedHandles.Count > 0 ? m_SelectedHandles.First() : default;
        
        /// <summary>
        /// Controls the selection type.
        /// </summary>
        /// <remarks>
        /// The default value is <see cref="float"/>.
        /// When you set the collection view to disable selections, any current selection is cleared.
        /// </remarks>
        public SelectionType SelectionType
        {
            get => m_SelectionType;
            set
            {
                m_SelectionType = value;

                switch (m_SelectionType)
                {
                    case SelectionType.None:
                    {
                        ClearSelection();
                        break;
                    }
                    case SelectionType.Single:
                    {
                        if (m_SelectedHandles.Count > 1) SetSelection(m_SelectedHandles.First());
                        break;
                    }
                }
            }
        }
        
        /// <summary>
        /// Callback triggered when the selection changes.
        /// </summary>
        /// <remarks>
        /// This callback receives an enumerable that contains the handles selected.
        /// </remarks>
        public Action<IEnumerable<HierarchyNodeHandle>> OnSelectedHandlesChanged { get; set; }
        
        // Used to store the focused element to enable scrolling without losing it.
        int m_LastFocusedElementIndex = -1;
        readonly List<int> m_LastFocusedElementTreeChildIndexes = new List<int>();

        void HandleFocus(HierarchyListViewItem item)
        {
            if (m_LastFocusedElementIndex == -1)
                return;

            if (m_LastFocusedElementIndex == item.Index)
                item.ElementAtTreePath(m_LastFocusedElementTreeChildIndexes)?.Focus();
            else
                item.ElementAtTreePath(m_LastFocusedElementTreeChildIndexes)?.Blur();
        }
        
        protected override void ExecuteDefaultAction(EventBase evt)
        {
            base.ExecuteDefaultAction(evt);
            
            // We always need to know when pointer up event occurred to reset DragEventsProcessor flags.
            // Some controls may capture the mouse, but the ListView is a composite root (isCompositeRoot),
            // and will always receive ExecuteDefaultAction despite what the actual event target is.
            if (evt.eventTypeId == PointerUpEvent.TypeId())
            {
                // m_Dragger?.OnPointerUpEvent((PointerUpEvent)evt);
            }
            // We need to store the focused item in order to be able to scroll out and back to it, without
            // seeing the focus affected. To do so, we store the path to the tree element that is focused,
            // and set it back in Setup().
            else if (evt.eventTypeId == FocusEvent.TypeId())
            {
                m_LastFocusedElementTreeChildIndexes.Clear();
                
                // @TODO use leafTarget
                var target = evt.target as VisualElement;

                if (m_ScrollView.contentContainer.FindElementInTree(target, m_LastFocusedElementTreeChildIndexes))
                {
                    var e = m_ScrollView.contentContainer[m_LastFocusedElementTreeChildIndexes[0]];
                    
                    foreach (var element in m_Elements)
                    {
                        if (element == e)
                        {
                            m_LastFocusedElementIndex = element.Index;
                            break;
                        }
                    }

                    m_LastFocusedElementTreeChildIndexes.RemoveAt(0);
                }
                else
                {
                    m_LastFocusedElementIndex = -1;
                }
            }
        }

        void OnMouseDown(MouseDownEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            DoSelect(evt.localMousePosition, evt.clickCount, evt.actionKey, evt.shiftKey);
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            var clickedIndex = GetIndexForPosition(evt.localMousePosition);

            if (clickedIndex < 0 || clickedIndex > m_Nodes.Count)
                return;
            
            var handle = GetHandleForIndex(clickedIndex);
            
            if (SelectionType == SelectionType.Multiple
                && !evt.shiftKey
                && !evt.actionKey
                && m_SelectedHandles.Count > 1
                && m_SelectedHandles.Contains(handle))
            {
                SetSelection(handle);
            }
        }
        
        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
                return;

            if (m_Nodes.Count == 0)
                return;

            var shouldStopPropagation = true;
            var shouldScroll = true;
            var selectedIndex = m_SelectedHandles.Count > 0 ? GetIndexForHandle(m_SelectedHandles[0]) : -1;

            switch (evt.keyCode)
            {
                case KeyCode.UpArrow:
                    if (selectedIndex > 0)
                        SetSelection(GetHandleForIndex(selectedIndex - 1));
                    break;
                case KeyCode.DownArrow:
                    if (selectedIndex + 1 < m_Nodes.Count)
                        SetSelection(GetHandleForIndex(selectedIndex + 1));
                    break;
                case KeyCode.Home:
                    SetSelection(GetHandleForIndex(0));
                    break;
                case KeyCode.End:
                    SetSelection(GetHandleForIndex(m_Nodes.Count - 1));
                    break;
                case KeyCode.Return:
                    // @TODO handle item chosen
                    break;
                case KeyCode.PageDown:
                    SetSelection(GetHandleForIndex(Math.Min(m_Nodes.Count - 1, selectedIndex + (int)(m_ScrollView.layout.height / GetResolvedItemHeight()))));
                    break;
                case KeyCode.PageUp:
                    SetSelection(GetHandleForIndex(Math.Max(0, selectedIndex - (int)(m_ScrollView.layout.height / GetResolvedItemHeight()))));
                    break;
                case KeyCode.A:
                    if (evt.actionKey)
                    {
                        // @TODO Select All
                        // SelectAll();
                        shouldScroll = false;
                    }
                    break;
                case KeyCode.Escape:
                    ClearSelection();
                    shouldScroll = false;
                    break;
                default:
                    shouldStopPropagation = false;
                    shouldScroll = false;
                    break;
            }

            if (shouldStopPropagation)
                evt.StopPropagation();

            if (shouldScroll)
                ScrollToItem(selectedIndex);
        }
        
        void ScrollToItem(int index)
        {
            if (m_Elements.Count == 0 || index < -1)
                return;

            var scrollViewHeight = m_ScrollView.layout.height;
            var pixelAlignedItemHeight = GetResolvedItemHeight();
            
            if (index == -1)
            {
                // Scroll to last item
                var actualCount = (int)(scrollViewHeight / pixelAlignedItemHeight);
                
                if (m_Nodes.Count < actualCount)
                    m_ScrollView.scrollOffset = new Vector2(0, 0);
                else
                    m_ScrollView.scrollOffset = new Vector2(0, m_Nodes.Count * pixelAlignedItemHeight);
            }
            else if (m_FirstVisibleIndex > index)
            {
                m_ScrollView.scrollOffset = Vector2.up * pixelAlignedItemHeight * index;
            }
            else // index >= first
            {
                var actualCount = (int)(scrollViewHeight / pixelAlignedItemHeight);
                
                if (index < m_FirstVisibleIndex + actualCount)
                    return;

                var someItemIsPartiallyVisible = (int)(scrollViewHeight - actualCount * pixelAlignedItemHeight) != 0;
                var d = index - actualCount;

                // we're scrolling down in that case
                // if the list view size is not an integer multiple of the item height
                // the selected item might be the last visible and truncated one
                // in that case, increment by one the index
                if (someItemIsPartiallyVisible)
                    d++;

                m_ScrollView.scrollOffset = Vector2.up * pixelAlignedItemHeight * d;
            }
        }
        
        void DoRangeSelection(int rangeSelectionFinalIndex)
        {
            var maxIndex = m_SelectedHandles.Max(h => m_Nodes.IndexOf(h));
            var minIndex = m_SelectedHandles.Min(h => m_Nodes.IndexOf(h));
            
            var selectionOrigin = m_IsRangeSelectionDirectionUp ? maxIndex : minIndex;

            ClearSelectionWithoutValidation();

            m_IsRangeSelectionDirectionUp = rangeSelectionFinalIndex < selectionOrigin;
            k_RangeSelectionBuffer.Clear();
            
            if (m_IsRangeSelectionDirectionUp)
            {
                for (var i = rangeSelectionFinalIndex; i <= selectionOrigin; i++)
                    k_RangeSelectionBuffer.Add(GetHandleForIndex(i));
            }
            else
            {
                for (var i = rangeSelectionFinalIndex; i >= selectionOrigin; i--)
                    k_RangeSelectionBuffer.Add(GetHandleForIndex(i));
            }

            AddToSelection(k_RangeSelectionBuffer);
        }
        
        void DoSelect(Vector2 localPosition, int clickCount, bool actionKey, bool shiftKey)
        {
            var clickedIndex = GetIndexForPosition(localPosition);
            
            if (clickedIndex < 0 || clickedIndex > m_Nodes.Count - 1)
                return;

            var clickedHandle = GetHandleForIndex(clickedIndex);
            
            switch (clickCount)
            {
                case 1:
                {
                    if (SelectionType == SelectionType.None)
                        return;

                    if (SelectionType == SelectionType.Multiple && actionKey)
                    {
                        // Add/remove single clicked element
                        if (m_SelectedHandles.Contains(clickedHandle))
                            RemoveFromSelection(clickedHandle);
                        else
                            AddToSelection(clickedHandle);
                    }
                    else if (SelectionType == SelectionType.Multiple && shiftKey)
                    {
                        if (m_SelectedHandles.Count == 0)
                        {
                            SetSelection(clickedHandle);
                        }
                        else
                        {
                            DoRangeSelection(clickedIndex);
                        }
                    }
                    else if (SelectionType == SelectionType.Multiple && m_SelectedHandles.Contains(clickedHandle))
                    {
                    }
                    else
                    {
                        SetSelection(clickedHandle);
                    }

                    break;
                }

                case 2:
                {
                    SetSelection(clickedHandle);
                    break;
                }
            }
        }
        
        public void AddToSelection(HierarchyNodeHandle handle)
        {
            AddToSelection(new[] { handle });
        }

        void AddToSelection(ICollection<HierarchyNodeHandle> handles)
        {
            if (handles == null || handles.Count == 0)
                return;

            foreach (var handle in handles)
            {
                AddToSelectionWithoutValidation(handle);
            }

            NotifyOfSelectionChange();
            SaveViewData();
        }

        void AddToSelectionWithoutValidation(HierarchyNodeHandle handle)
        {
            if (m_SelectedHandles.Contains(handle))
                return;

            foreach (var elements in m_Elements)
                if (elements.Handle == handle)
                    elements.SetSelected(true);

            m_SelectedHandles.Add(handle);
        }

        void RemoveFromSelection(HierarchyNodeHandle handle)
        {
            RemoveFromSelectionWithoutValidation(handle);
            NotifyOfSelectionChange();
            SaveViewData();
        }

        void RemoveFromSelectionWithoutValidation(HierarchyNodeHandle handle)
        {
            if (!m_SelectedHandles.Contains(handle))
                return;

            foreach (var elements in m_Elements)
                if (elements.Handle == handle)
                    elements.SetSelected(false);

            m_SelectedHandles.Remove(handle);
        }

        public void SetSelection(HierarchyNodeHandle handle)
        {
            k_SingleSelectionBuffer[0] = handle;
            SetSelectionInternal(k_SingleSelectionBuffer, true);
        }

        public void SetSelection(IEnumerable<HierarchyNodeHandle> handles)
        {
            SetSelectionInternal(handles, true);
        }
        
        public void SetSelectionWithoutNotify(HierarchyNodeHandle handle)
        {
            k_SingleSelectionBuffer[0] = handle;
            SetSelectionInternal(k_SingleSelectionBuffer, false);
        }

        public void SetSelectionWithoutNotify(IEnumerable<HierarchyNodeHandle> handles)
        {
            SetSelectionInternal(handles, false);
        }

        void SetSelectionInternal(IEnumerable<HierarchyNodeHandle> handles, bool sendNotification)
        {
            if (handles == null)
                throw new ArgumentNullException();

            ClearSelectionWithoutValidation();
            
            foreach (var handle in handles)
                AddToSelectionWithoutValidation(handle);

            if (sendNotification)
                NotifyOfSelectionChange();

            SaveViewData();
        }

        void NotifyOfSelectionChange()
        {
            OnSelectedHandlesChanged?.Invoke(m_SelectedHandles);
        }
        
        /// <summary>
        /// Deselects any selected items.
        /// </summary>
        public void ClearSelection()
        {
            if (m_SelectedHandles.Count == 0)
                return;

            ClearSelectionWithoutValidation();
            NotifyOfSelectionChange();
        }
        
        void ClearSelectionWithoutValidation()
        {
            foreach (var element in m_Elements)
                element.SetSelected(false);

            m_SelectedHandles.Clear();
        }
    }
}