using System;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class EntityView : VisualElement
    {
        const string k_IconId = "Icon";
        const string k_NameId = "Name";

        readonly Label m_EntityName;
        readonly VisualElement m_EntityIcon;
        EntityViewData m_Data;
        bool m_IsPrefab;

        public EntityView(EntityViewData data)
        {
            m_IsPrefab = false;

            Resources.Templates.EntityView.Clone(this);

            m_EntityName = this.Q<Label>(k_NameId);
            m_EntityIcon = this.Q<VisualElement>(k_IconId);
            if (data.Entity != default)
                Update(data);
            this.Q<VisualElement>(className: UssClasses.EntityView.GoTo)
                .RegisterCallback<MouseDownEvent, EntityView>((_, @this) =>
                {
                    Analytics.SendEditorEvent(Analytics.Window.Inspector, Analytics.EventType.RelationshipGoTo, Analytics.GoToEntityDestination);
                    EntitySelectionProxy.SelectEntity(@this.m_Data.World, @this.m_Data.Entity);
                }, this);
        }

        public void Update(EntityViewData data)
        {
            m_Data = data;
            m_EntityName.text = data.EntityName;

            var entity = data.Entity;
            var isPrefab = data.World.EntityManager.HasComponent<Prefab>(entity);

            if (m_IsPrefab != isPrefab)
            {
                if (isPrefab)
                {
                    m_EntityIcon.RemoveFromClassList(UssClasses.EntityView.EntityIconEntity);
                    m_EntityName.RemoveFromClassList(UssClasses.EntityView.EntityNameEntity);
                    m_EntityIcon.AddToClassList(UssClasses.EntityView.EntityIconPrefab);
                    m_EntityName.AddToClassList(UssClasses.EntityView.EntityNamePrefab);
                }
                else
                {
                    m_EntityIcon.RemoveFromClassList(UssClasses.EntityView.EntityIconPrefab);
                    m_EntityName.RemoveFromClassList(UssClasses.EntityView.EntityNamePrefab);
                    m_EntityIcon.AddToClassList(UssClasses.EntityView.EntityIconEntity);
                    m_EntityName.AddToClassList(UssClasses.EntityView.EntityNameEntity);
                }

                m_IsPrefab = isPrefab;
            }
        }
    }
}
