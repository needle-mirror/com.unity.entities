using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Unity.Properties;
using Unity.Properties.UI;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class ComponentsTab : ITabContent
    {
        readonly EntityInspectorContext m_Context;
        bool m_IsVisible;

        public string TabName { get; } = L10n.Tr("Components");
        public void OnTabVisibilityChanged(bool isVisible) => m_IsVisible = isVisible;

        public ComponentsTab(EntityInspectorContext entityInspectorContext)
        {
            m_Context = entityInspectorContext;
        }

        [UsedImplicitly]
        class ComponentTabsInspector : Inspector<ComponentsTab>
        {
            readonly List<string> m_Filters = new List<string>();
            readonly EntityInspectorComponentOrder m_CurrentComponentOrder = new EntityInspectorComponentOrder();
            readonly EntityInspectorStructureVisitor m_UpdateVisitor = new EntityInspectorStructureVisitor();

            EntityInspectorVisitor m_InspectorVisitor;
            TagComponentContainer m_TagsRoot;
            VisualElement m_Root;

            public override VisualElement Build()
            {
                m_Root = Resources.Templates.Inspector.ComponentsTab.Clone();
                var componentSearchField = m_Root.Q<ToolbarSearchField>(className: UssClasses.Inspector.ComponentsTab.SearchField);
                componentSearchField.RegisterCallback<ChangeEvent<string>, ComponentTabsInspector>((evt, ct) =>
                {
                    ct.m_Filters.Clear();
                    var value = evt.newValue.Trim();
                    var matches = value.Split(' ');
                    foreach (var match in matches)
                    {
                        ct.m_Filters.Add(match);
                    }

                    ct.SearchChanged();
                }, this);

                m_TagsRoot = new TagComponentContainer(Target.m_Context);
                m_Root.Add(m_TagsRoot);

                m_Root.RegisterCallback<GeometryChangedEvent, VisualElement>((evt, elem) =>
                {
                    StylingUtility.AlignInspectorLabelWidth(elem);
                }, m_Root);

                m_InspectorVisitor = new EntityInspectorVisitor(m_Root, m_TagsRoot, Target.m_Context);
                PropertyContainer.Visit(Target.m_Context.EntityContainer, m_InspectorVisitor);

                return m_Root;
            }

            public override void Update()
            {
                if (!Target.m_IsVisible)
                    return;

                m_UpdateVisitor.Reset();
                var container = Target.m_Context.EntityContainer;
                PropertyContainer.Visit(ref container, m_UpdateVisitor);
                UpdateComponentOrder(m_UpdateVisitor.ComponentOrder);
            }

            void SearchChanged()
            {
                using (var pooled = PooledList<ComponentElementBase>.Make())
                {
                    var list = pooled.List;
                    m_Root.Query<ComponentElementBase>().ToList(list);
                    if (m_Filters.Count == 0)
                        list.ForEach(ce => ce.Show());
                    else
                    {
                        list.ForEach(ce =>
                        {
                            var showShow = false;
                            foreach (var token in m_Filters)
                            {
                                if (ce.Path.IndexOf(token, StringComparison.InvariantCultureIgnoreCase) >= 0)
                                {
                                    showShow = true;
                                    break;
                                }
                            }

                            if (showShow)
                                ce.Show();
                            else
                                ce.Hide();
                        });
                    }
                }
            }

            void UpdateComponentOrder(EntityInspectorComponentOrder current)
            {
                m_CurrentComponentOrder.Reset();
                using (var pooledElements = PooledList<ComponentElementBase>.Make())
                {
                    ComputeCurrentComponentOrder(m_CurrentComponentOrder, pooledElements);

                    if (current == m_CurrentComponentOrder)
                        return;

                    // Component removed since the last update
                    using (var pooled = ComputeRemovedComponents(current.Components, m_CurrentComponentOrder.Components))
                    {
                        var list = pooled.List;
                        foreach (var path in list)
                        {
                            var element = pooledElements.List.Find(ce => ce.Path == path);
                            element?.RemoveFromHierarchy();
                        }
                    }

                    // Tags removed since the last update
                    using (var pooled = ComputeRemovedComponents(current.Tags, m_CurrentComponentOrder.Tags))
                    {
                        var list = pooled.List;
                        foreach (var path in list)
                        {
                            var element = pooledElements.List.Find(ce => ce.Path == path);
                            element?.RemoveFromHierarchy();
                        }
                    }

                    // Component added since the last update
                    using (var pooled = ComputeAddedComponents(current.Components, m_CurrentComponentOrder.Components))
                    {
                        var list = pooled.List;
                        var container = Target.m_Context.EntityContainer;
                        foreach (var path in list)
                        {
                            PropertyContainer.Visit(ref container, m_InspectorVisitor, new PropertyPath(path));
                        }
                    }

                    // Tags removed since the last update
                    using (var pooled = ComputeAddedComponents(current.Tags, m_CurrentComponentOrder.Tags))
                    {
                        var list = pooled.List;
                        var container = Target.m_Context.EntityContainer;
                        foreach (var path in list)
                        {
                            PropertyContainer.Visit(ref container, m_InspectorVisitor, new PropertyPath(path));
                        }
                    }
                }
            }

            void ComputeCurrentComponentOrder(EntityInspectorComponentOrder info, List<ComponentElementBase> elements)
            {
                elements.Clear();
                m_Root.Query<ComponentElementBase>().ToList(elements);
                foreach (var ce in elements)
                {
                    if (ce.Type == ComponentPropertyType.Tag)
                        info.Tags.Add(ce.Path);
                    else
                        info.Components.Add(ce.Path);
                }
            }

            static PooledList<string> ComputeAddedComponents(IEnumerable<string> lhs, IEnumerable<string> rhs)
            {
                return Except(lhs, rhs);
            }

            static PooledList<string> ComputeRemovedComponents(IEnumerable<string> lhs, IEnumerable<string> rhs)
            {
                return Except(rhs, lhs);
            }

            static PooledList<string> Except(IEnumerable<string> lhs, IEnumerable<string> rhs)
            {
                return lhs.Except(rhs).ToPooledList();
            }
        }
    }
}
