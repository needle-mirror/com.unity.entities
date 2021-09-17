using System;
using System.Collections.Generic;
using Unity.Editor.Bridge;
using UnityEngine;
using UnityEngine.UIElements;
using HierarchyModel = Unity.Entities.Editor.Hierarchy;

namespace Unity.Entities.Editor
{
    partial class HierarchyListView : BindableElementBridge
    {
        const int k_ExtraVisibleItems = 2;
        
        /// <summary>
        /// The hierarchy model this view is rendering.
        /// </summary>
        readonly HierarchyModel m_Model;
        
        /// <summary>
        /// The 'IList' source we are showing in the tree.
        /// </summary>
        readonly HierarchyNodes m_Nodes;
        
        /// <summary>
        /// The main scroll view driving the tree view..
        /// </summary>
        readonly ScrollView m_ScrollView;
        
        /// <summary>
        /// The set of virtualized row elements.
        /// </summary>
        readonly List<HierarchyListViewItem> m_Elements;
        
        /// <summary>
        /// The pool used to recycle row elements.
        /// </summary>
        readonly BasicPool<HierarchyListViewItem> m_Pool;
        
        int m_FirstVisibleIndex;
        int m_ItemHeight = 16;
        float m_ScrollOffset;
        
        /// <summary>
        /// Callback invoked whenever a new row element is created.
        /// </summary>
        public Action<HierarchyListViewItem> OnMakeItem { get; set; }

        public override VisualElement contentContainer => m_ScrollView.contentContainer;

        public HierarchyListView(HierarchyModel model)
        {
            m_Model = model;
            m_Nodes = model.GetNodes();
            
            m_Elements = new List<HierarchyListViewItem>();
            m_Pool = new BasicPool<HierarchyListViewItem>(() =>
            {
                var item = new HierarchyListViewItem(m_Model);
                OnMakeItem?.Invoke(item);
                return item;
            });
            
            AddToClassList(UnityEngine.UIElements.ListView.ussClassName);
            
            m_ScrollOffset = 0.0f;
            
            m_ScrollView = new ScrollView
            {
                viewDataKey = "list-view__scroll-view"
            };
            
            m_ScrollView.StretchToParentSize();
            m_ScrollView.verticalScroller.valueChanged += OnScroll;
            
            m_ScrollView.contentContainer.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_ScrollView.contentContainer.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            hierarchy.Add(m_ScrollView);
            
            m_ScrollView.contentContainer.focusable = true;
            m_ScrollView.contentContainer.usageHints &= ~UsageHints.GroupTransform; // Scroll views with virtualized content shouldn't have the "view transform" optimization

            RegisterCallback<GeometryChangedEvent>(OnSizeChanged);
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

            focusable = true;
            isCompositeRoot = true;
            delegatesFocus = true;
        }
        
        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.destinationPanel == null)
                return;
            
            m_ScrollView.contentContainer.RegisterCallback<MouseDownEvent>(OnMouseDown);
            m_ScrollView.contentContainer.RegisterCallback<MouseUpEvent>(OnMouseUp);
            m_ScrollView.contentContainer.RegisterCallback<KeyDownEvent>(OnKeyDown);
            
            // @FIXME Pointer events seems to have a strange interaction with Toggles. Reverting to MouseEvents until it's fixed.
            /*
            m_ScrollView.contentContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_ScrollView.contentContainer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.contentContainer.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_ScrollView.contentContainer.RegisterCallback<PointerUpEvent>(OnPointerUp);
            */
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            if (evt.originPanel == null)
                return;
            
            m_ScrollView.contentContainer.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            m_ScrollView.contentContainer.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            m_ScrollView.contentContainer.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            
            // @FIXME Pointer events seems to have a strange interaction with Toggles. Reverting to MouseEvents until it's fixed.
            /*
            m_ScrollView.contentContainer.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            m_ScrollView.contentContainer.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.contentContainer.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
            m_ScrollView.contentContainer.RegisterCallback<PointerUpEvent>(OnPointerUp);
            */
        }
        
        void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            // @TODO support for custom item height.
        }

        void OnSizeChanged(GeometryChangedEvent evt)
        {
            if (Mathf.Approximately(evt.newRect.width, evt.oldRect.width) && Mathf.Approximately(evt.newRect.height, evt.oldRect.height))
                return;

            Resize(evt.newRect.height);
        }

        void OnScroll(float offset)
        {
            var pixelAlignedItemHeight = GetResolvedItemHeight();
            var firstVisibleItemIndex = (int)(offset / pixelAlignedItemHeight);
            
            m_ScrollView.contentContainer.style.paddingTop = firstVisibleItemIndex * pixelAlignedItemHeight;
            m_ScrollView.contentContainer.style.height = m_Nodes.Count * pixelAlignedItemHeight;
            m_ScrollOffset = offset;

            if (firstVisibleItemIndex == m_FirstVisibleIndex)
                return;
            
            m_FirstVisibleIndex = firstVisibleItemIndex;

            if (m_Elements.Count > 0)
            {
                if (m_FirstVisibleIndex < m_Elements[0].Index) // we're scrolling up
                {
                    using var insertion = PooledList<HierarchyListViewItem>.Make();
                    
                    var count = m_Elements[0].Index - m_FirstVisibleIndex;

                    for (var i = 0; i < count && m_Elements.Count > 0; ++i)
                    {
                        var last = m_Elements[m_Elements.Count - 1];
                        insertion.List.Add(last);
                        m_Elements.RemoveAt(m_Elements.Count - 1); // we remove from the end

                        last.SendToBack(); // We send the element to the top of the list (back in z-order)
                    }

                    m_Elements.InsertRange(0, insertion.List);
                }
                else
                {
                    if (m_FirstVisibleIndex < m_Elements[m_Elements.Count - 1].Index)
                    {
                        using var insertion = PooledList<HierarchyListViewItem>.Make();

                        var checkIndex = 0;
                        
                        while (m_FirstVisibleIndex > m_Elements[checkIndex].Index)
                        {
                            var first = m_Elements[checkIndex];
                            insertion.List.Add(first);
                            checkIndex++;

                            first.BringToFront(); //We send the element to the bottom of the list (front in z-order)
                        }

                        m_Elements.RemoveRange(0, checkIndex); //we remove them all at once
                        m_Elements.AddRange(insertion.List); // add them back to the end
                    }
                }
                
                for (var i = 0; i < m_Elements.Count; i++)
                    BindElement(m_Elements[i], m_FirstVisibleIndex + i);
            }
        }

        /// <summary>
        /// Clears all elements and does a full rebuild.
        /// </summary>
        public void Rebuild()
        {
            // Unbind and destroy all elements.
            foreach (var element in m_Elements)
            {
                UnbindElement(element);
                element.RemoveFromHierarchy();
            }

            m_FirstVisibleIndex = 0;
            m_Pool.Clear();
            m_Elements.Clear();
            m_ScrollView.Clear();
            
            var height= m_ScrollView.layout.height;

            if (float.IsNaN(height))
                return;

            Resize(height);
        }

        /// <summary>
        /// Rebinds currently visible elements.
        /// </summary>
        /// <remarks>
        /// Use this method whenever the underlying data source changes.
        /// </remarks>
        public void Refresh()
        {
            for (var i = 0; i < m_Elements.Count; i++)
            {
                var index = m_FirstVisibleIndex + i;
                var element = m_Elements[i];

                if ((uint) index < m_Nodes.Count)
                {
                    BindElement(element, index);
                }
                else
                {
                    ReleaseElement(element);
                    m_Elements.RemoveAt(i--);
                }
            }
            
            var height= m_ScrollView.layout.height;

            if (float.IsNaN(height))
                return;

            Resize(height);
        }

        /// <summary>
        /// Resizes the scroll view to the specified height.
        /// </summary>
        /// <remarks>
        /// This method will add or remove virtualized tree view elements as needed.
        /// </remarks>
        /// <param name="height">The height to resize to.</param>
        void Resize(float height)
        {
            var pixelAlignedItemHeight = GetResolvedItemHeight();
            var contentHeight = m_Nodes.Count * pixelAlignedItemHeight;
            
            m_ScrollView.contentContainer.style.height = contentHeight;
            
            // Restore scroll offset and preemptively update the highValue
            // in case this is the initial restore from persistent data and
            // the ScrollView's OnGeometryChanged() didn't update the low and highValues.
            var scrollableHeight = Mathf.Max(0, contentHeight - m_ScrollView.contentViewport.layout.height);
            
            m_ScrollView.verticalScroller.slider.highValue = scrollableHeight;
            m_ScrollView.verticalScroller.slider.value = Mathf.Min(m_ScrollOffset, m_ScrollView.verticalScroller.highValue);
            
            var itemCount = Mathf.Min((int)(height / pixelAlignedItemHeight) + k_ExtraVisibleItems, m_Nodes.Count);
            var elementCount = m_Elements.Count;

            if (elementCount == itemCount)
            {
                return;
            }

            if (elementCount > itemCount)
            {
                var removeCount = elementCount - itemCount;
                
                for (var i = 0; i < removeCount; i++)
                {
                    var lastIndex = m_Elements.Count - 1;
                    ReleaseElement(m_Elements[lastIndex]);
                    m_Elements.RemoveAt(lastIndex);
                }
            }
            else
            {
                var addCount = itemCount - elementCount;
                
                for (var i = 0; i < addCount; i++)
                {
                    var index = i + m_FirstVisibleIndex + elementCount;
                    var element = AcquireElement();
                    m_Elements.Add(element);
                    Add(element);
                    BindElement(element, index);
                }
            }
        }

        /// <summary>
        /// Gets or creates a new tree view element from the pool.
        /// </summary>
        /// <returns></returns>
        HierarchyListViewItem AcquireElement()
        {
            var element = m_Pool.Acquire();
            element.style.height = GetResolvedItemHeight();
            element.Attach();
            return element;
        }

        /// <summary>
        /// Releases an existing element to the pool.
        /// </summary>
        /// <param name="element">The element to release.</param>
        void ReleaseElement(HierarchyListViewItem element)
        {
            element.Detach();
            UnbindElement(element);
            m_Pool.Release(element);
        }

        /// <summary>
        /// Binds the specified element to data source.
        /// </summary>
        /// <param name="element">The element to unbind.</param>
        /// <param name="index">The data source index to bind to.</param>
        void BindElement(HierarchyListViewItem element, int index)
        {
            if (index >= m_Nodes.Count)
            {
                element.style.display = DisplayStyle.None;
                UnbindElement(element);
                return;
            }
            
            var node = m_Nodes[index];

            if (!element.IsChanged(node))
            {
                // The element is already bound to this handle _with_ the same version. Nothing to do.
                return;
            }
            
            element.style.display = DisplayStyle.Flex;

            if (element.Index != -1)
            {
                // Unbind the previous data; if any.
                UnbindElement(element);
            }
            
            var indexInParent = index - m_FirstVisibleIndex;

            if (indexInParent >= m_ScrollView.contentContainer.childCount)
            {
                element.BringToFront();
            }
            else if (indexInParent >= 0)
            {
                element.PlaceBehind(m_ScrollView.contentContainer[indexInParent]);
            }
            else
            {
                element.SendToBack();
            }

            element.Bind(index, node);
            element.SetSelected(m_SelectedHandles.Contains(node.GetHandle()));
            
            element.pseudoStates &= ~2; // PseudoStates.Hover
            HandleFocus(element);
        }

        /// <summary>
        /// Unbinds the specified element from it's data source.
        /// </summary>
        /// <param name="element">The element to unbind.</param>
        void UnbindElement(HierarchyListViewItem element)
        {
            element.Unbind();
        }

        float GetResolvedItemHeight()
        {
            var dpiScaling = scaledPixelsPerPoint;
            return Mathf.Round(m_ItemHeight * dpiScaling) / dpiScaling;
        }

        int GetIndexForPosition(Vector2 position)
        {
            return (int)(position.y / GetResolvedItemHeight());
        }

        HierarchyNodeHandle GetHandleForIndex(int index)
        {
            return m_Nodes[index].GetHandle();
        }

        int GetIndexForHandle(HierarchyNodeHandle handle)
        {
            return m_Nodes.IndexOf(handle);
        }
    }
}