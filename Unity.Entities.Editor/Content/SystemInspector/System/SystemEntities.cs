using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Properties.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class SystemEntities
    {
        readonly World m_World;
        SystemProxy m_SystemProxy;
        UnsafeList<EntityQuery> m_LastEntityQueries;

        public SystemEntities(World world, SystemProxy systemProxy)
        {
            m_World = world;
            m_SystemProxy = systemProxy;
            m_EntitiesFromQueries = new List<QueryWithEntitiesViewData>();
        }

        readonly List<QueryWithEntitiesViewData> m_EntitiesFromQueries;
        bool m_IsVisible;

        public unsafe List<QueryWithEntitiesViewData> EntitiesFromQueries
        {
            get
            {
                if (!m_World.IsCreated || !m_SystemProxy.Valid)
                {
                    m_EntitiesFromQueries.Clear();
                    return m_EntitiesFromQueries;
                }

                var ptr = m_SystemProxy.StatePointerForQueryResults;
                if (ptr == null)
                {
                    m_EntitiesFromQueries.Clear();
                    return m_EntitiesFromQueries;
                }

                var currentQueries = ptr->EntityQueries;
                if (m_LastEntityQueries.Equals(currentQueries))
                    return m_EntitiesFromQueries;

                m_LastEntityQueries = currentQueries;
                m_EntitiesFromQueries.Clear();

                for (var i = 0; i < m_LastEntityQueries.Length; i++)
                {
                    m_EntitiesFromQueries.Add(new QueryWithEntitiesViewData(m_World, m_LastEntityQueries[i], m_SystemProxy, i + 1));
                }

                return m_EntitiesFromQueries;
            }
        }

        public void OnTabVisibilityChanged(bool isVisible) => m_IsVisible = isVisible;


        [UsedImplicitly]
        class SystemEntitiesInspector : Inspector<SystemEntities>
        {
            readonly Cooldown m_Cooldown = new Cooldown(TimeSpan.FromMilliseconds(Constants.Inspector.CoolDownTime));
            readonly List<QueryWithEntitiesView> m_Views = new List<QueryWithEntitiesView>();

            public override VisualElement Build()
            {
                var section = new FoldoutWithoutActionButton
                {
                    HeaderName = {text = L10n.Tr("Entities")}
                };

                foreach (var queryEntities in Target.EntitiesFromQueries)
                {
                    var queryWithEntities = new QueryWithEntitiesView(queryEntities);
                    m_Views.Add(queryWithEntities);
                    section.Add(queryWithEntities);
                }

                Update();

                return section;
            }

            public override void Update()
            {
                if (!Target.m_IsVisible || !m_Cooldown.Update(DateTime.UtcNow))
                    return;

                foreach (var view in m_Views)
                {
                    view.Update();
                }
            }
        }
    }
}
