using System;
using System.Collections.Generic;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    class EntityHierarchyItemView : VisualElement
    {
        public const int DisabledStateRefreshPeriodInMs = 150;

        static readonly string k_PingSubSceneInHierarchy = L10n.Tr("Ping sub scene in hierarchy");
        static readonly string k_PingSubSceneInProjectWindow = L10n.Tr("Ping sub scene in project window");

        public static readonly HashSet<EntityHierarchyItemView> ItemsScheduledForPeriodicCheck = new HashSet<EntityHierarchyItemView>();

        readonly VisualElement m_Icon;
        readonly Label m_NameLabel;
        readonly VisualElement m_SystemButton;
        readonly VisualElement m_PingGameObject;

        EntityHierarchyNodeId m_ItemNode;

        int? m_OriginatingId;
        GameObject m_OriginatingGameObject;
        IManipulator m_ContextMenuManipulator;

        public EntityHierarchyItemView()
        {
            Resources.Templates.EntityHierarchyItem.Clone(this);
            AddToClassList(UssClasses.DotsEditorCommon.CommonResources);

            m_Icon = this.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Item.Icon);
            m_NameLabel = this.Q<Label>(className: UssClasses.EntityHierarchyWindow.Item.NameLabel);
            m_SystemButton = this.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Item.SystemButton);
            m_PingGameObject = this.Q<VisualElement>(className: UssClasses.EntityHierarchyWindow.Item.PingGameObjectButton);
        }

        public void SetSource(in EntityHierarchyNodeId item, IEntityHierarchy entityHierarchy)
        {
            // Reset is not automatically called by the owning view when scrolling, it only calls SetSource on an existing instance with a new underlying data.
            // We need to call Reset here to make sure we don't keep styling and behaviors from a previous node that can be from a different kind.
            Reset();

            m_ItemNode = item;

            switch (item.Kind)
            {
                case NodeKind.Entity:
                {
                    RenderEntityNode(entityHierarchy);
                    break;
                }
                case NodeKind.Scene:
                case NodeKind.RootScene:
                case NodeKind.SubScene:
                case NodeKind.DynamicSubScene:
                {
                    RenderSceneNode(entityHierarchy);
                    break;
                }
                case NodeKind.Custom:
                {
                    RenderCustomNode(entityHierarchy);
                    break;
                }
                case NodeKind.Root:
                case NodeKind.None:
                {
                    RenderInvalidNode();
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public void Reset()
        {
            ItemsScheduledForPeriodicCheck.Remove(this);
            SetDisabledEntityStyle(false);
            RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.Prefab);
            RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.PrefabRoot);
            RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.SceneNode);

            m_NameLabel.RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.NameScene);

            m_Icon.RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.IconScene);
            m_Icon.RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.IconEntity);

            m_SystemButton.RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.VisibleOnHover);
            m_PingGameObject.RemoveFromClassList(UssClasses.EntityHierarchyWindow.Item.VisibleOnHover);

            m_PingGameObject.UnregisterCallback<MouseUpEvent>(OnPingGameObjectRequested);

            if (m_ContextMenuManipulator != null)
            {
                this.RemoveManipulator(m_ContextMenuManipulator);
                UnregisterCallback<ContextualMenuPopulateEvent>(OnSceneContextMenu);
                m_ContextMenuManipulator = null;
            }

            m_OriginatingId = null;
            m_OriginatingGameObject = null;
        }

        static bool IsEntityValid(EntityManager entityManager, Entity entity)
            => entity.Index > 0 && entity.Index < entityManager.EntityCapacity && entityManager.Exists(entity);

        public bool TryPerformPeriodicCheck(IEntityHierarchy entityHierarchy)
        {
            if (entityHierarchy.World == null || !entityHierarchy.World.IsCreated)
                return false;

            var entity = m_ItemNode.ToEntity();
            var entityManager = entityHierarchy.World.EntityManager;
            if (!IsEntityValid(entityManager, entity))
                return false;

            SetDisabledEntityStyle(entityManager.HasComponent<Disabled>(entity));

            var originatingGameObjectExists = (bool)m_OriginatingGameObject;
            var isPartOfAnyPrefab = originatingGameObjectExists && PrefabUtility.IsPartOfAnyPrefab(m_OriginatingGameObject)
                || entityManager.HasComponent<Prefab>(entity);

            var isPrefabRoot =
                // converted from an instance of a prefab
                originatingGameObjectExists && PrefabUtility.IsAnyPrefabInstanceRoot(m_OriginatingGameObject)
                ||
                // prefab asset being present in the hierarchy
                entityManager.HasComponent<Prefab>(entity) && entityManager.HasComponent<LinkedEntityGroup>(entity);

            EnableInClassList(UssClasses.EntityHierarchyWindow.Item.Prefab, isPartOfAnyPrefab);
            EnableInClassList(UssClasses.EntityHierarchyWindow.Item.PrefabRoot, isPrefabRoot);
            return true;
        }

        void RenderEntityNode(IEntityHierarchy entityHierarchy)
        {
            ItemsScheduledForPeriodicCheck.Add(this);

            m_NameLabel.text = entityHierarchy.State.GetNodeName(m_ItemNode);
            m_Icon.AddToClassList(UssClasses.EntityHierarchyWindow.Item.IconEntity);

            if (TryGetSourceGameObjectId(m_ItemNode.ToEntity(), entityHierarchy.World, out var originatingId))
            {
                m_OriginatingId = originatingId;
                m_PingGameObject.AddToClassList(UssClasses.EntityHierarchyWindow.Item.VisibleOnHover);
                m_PingGameObject.RegisterCallback<MouseUpEvent>(OnPingGameObjectRequested);

                if (originatingId != null && !EditorApplication.isPlaying)
                    m_OriginatingGameObject = EditorUtility.InstanceIDToObject(originatingId.Value) as GameObject;
            }

            TryPerformPeriodicCheck(entityHierarchy);
        }

        void SetDisabledEntityStyle(bool isDisabled) => style.opacity = isDisabled ? .5f : 1f;

        void RenderSceneNode(IEntityHierarchy entityHierarchy)
        {
            AddToClassList(UssClasses.EntityHierarchyWindow.Item.SceneNode);
            m_Icon.AddToClassList(UssClasses.EntityHierarchyWindow.Item.IconScene);
            m_NameLabel.AddToClassList(UssClasses.EntityHierarchyWindow.Item.NameScene);
            m_NameLabel.text = entityHierarchy.State.GetNodeName(m_ItemNode);

            if (m_ItemNode.Kind == NodeKind.SubScene)
            {
                m_ContextMenuManipulator = new ContextualMenuManipulator(null);
                this.AddManipulator(m_ContextMenuManipulator);
                RegisterCallback<ContextualMenuPopulateEvent>(OnSceneContextMenu);
            }
        }

        void RenderCustomNode(IEntityHierarchy entityHierarchy)
        {
            // TODO: Eventually, add a generic icon and a style that are overridable
            m_NameLabel.text = entityHierarchy.State.GetNodeName(m_ItemNode);
        }

        void RenderInvalidNode()
        {
            m_NameLabel.text = $"<UNKNOWN> ({m_ItemNode.ToString()})";
        }

        static bool TryGetSourceGameObjectId(Entity entity, World world, out int? originatingId)
        {
            if (!world.EntityManager.SafeExists(entity) || !world.EntityManager.HasComponent<EntityGuid>(entity))
            {
                originatingId = null;
                return false;
            }

            originatingId = world.EntityManager.GetComponentData<EntityGuid>(entity).OriginatingId;
            return true;
        }

        void OnSceneContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction(k_PingSubSceneInHierarchy, OnPingSubSceneInHierarchy);
            evt.menu.AppendAction(k_PingSubSceneInProjectWindow, OnPingSubSceneAsset);
        }

        void OnPingGameObjectRequested(MouseUpEvent _)
        {
            if (!m_OriginatingId.HasValue)
                return;

            EditorGUIUtility.PingObject(m_OriginatingId.Value);
        }

        void OnPingSubSceneInHierarchy(DropdownMenuAction obj)
            => EditorGUIUtility.PingObject(m_ItemNode.Id);

        void OnPingSubSceneAsset(DropdownMenuAction obj)
        {
            var subSceneObject = EditorUtility.InstanceIDToObject(m_ItemNode.Id);
            if (subSceneObject == null || !subSceneObject || !(subSceneObject is GameObject subSceneGameObject))
                return;

            var subScene = subSceneGameObject.GetComponent<SubScene>();
            if (subScene == null || !subScene || subScene.SceneAsset == null || !subScene.SceneAsset)
                return;

            EditorGUIUtility.PingObject(subScene.SceneAsset);
        }
    }
}
