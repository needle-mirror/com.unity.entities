using System;
using Unity.Burst;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

namespace Unity.Entities.Editor
{
    [Flags]
    enum NodeKind
    {
        None             = 0,
        Root             = 1 << 0,
        Entity           = 1 << 1,
        GameObject       = 1 << 2,
        Scene            = 1 << 3,
        RootScene        = 1 << 4 | Scene,
        SubScene         = 1 << 5 | Scene,
        DynamicSubScene  = 1 << 6 | Scene,
        Custom           = 1 << 7
    }

    /// <summary>
    /// A <see cref="HierarchyNodeHandle"/> specifies the storage type and location for a specific node within the <see cref="HierarchyNodeStore"/>.
    /// </summary>
    [Serializable]
    struct HierarchyNodeHandle : IEquatable<HierarchyNodeHandle>, IComparable<HierarchyNodeHandle>
    {
        public NodeKind Kind;
        public int Index;
        public int Version;

        internal HierarchyNodeHandle(NodeKind kind, int index = 0, int version = 0)
        {
            if (kind == NodeKind.Entity && version == 0)
                throw new ArgumentException($"Invalid version for entity node.");

            Kind = kind;
            Index = index;
            Version = version;
        }

        public bool Equals(HierarchyNodeHandle other) => Kind == other.Kind && Index == other.Index && Version == other.Version;
        public override bool Equals(object obj) => obj is HierarchyNodeHandle other && Equals(other);
        public static bool operator ==(HierarchyNodeHandle lhs, HierarchyNodeHandle rhs) => lhs.Equals(rhs);
        public static bool operator !=(HierarchyNodeHandle lhs, HierarchyNodeHandle rhs) => !(lhs == rhs);

        public int CompareTo(HierarchyNodeHandle other)
        {
            var value = ((byte) Kind).CompareTo((byte) other.Kind);
            if (value != 0) return value;
            value = Index.CompareTo(other.Index);
            return value != 0 ? value : Version.CompareTo(other.Version);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ Index;
                hashCode = (hashCode * 397) ^ Version;
                return hashCode;
            }
        }

        public static readonly HierarchyNodeHandle Root = new HierarchyNodeHandle(NodeKind.Root);

        public static HierarchyNodeHandle FromEntity(Entity entity)
            => new HierarchyNodeHandle(NodeKind.Entity, entity.Index, entity.Version);

        public static HierarchyNodeHandle FromGameObject(GameObject gameObject)
            => new HierarchyNodeHandle(NodeKind.GameObject, gameObject.GetInstanceID());

        public static HierarchyNodeHandle FromGameObject(int instanceId)
            => new HierarchyNodeHandle(NodeKind.GameObject, instanceId);

        public static HierarchyNodeHandle FromScene(UnityEngine.SceneManagement.Scene scene)
            => new HierarchyNodeHandle(NodeKind.Scene, index: scene.handle);

        public static HierarchyNodeHandle FromScene(UnloadedScene scene)
            => new HierarchyNodeHandle(NodeKind.Scene, index: scene.handle);

        public static unsafe HierarchyNodeHandle FromSubScene(SubSceneNodeMapping mapping, EntityDataAccess* access, Entity entity)
            => new HierarchyNodeHandle(NodeKind.SubScene, index: mapping.GetSubSceneIdFromSceneEntity(entity, access));

        public static unsafe HierarchyNodeHandle FromSubScene(SubSceneNodeMapping mapping, EntityDataAccess* access, SceneTag sceneTag)
            => new HierarchyNodeHandle(NodeKind.SubScene, index: mapping.GetSubSceneIdFromSceneEntity(access->GetComponentData<SceneEntityReference>(sceneTag.SceneEntity).SceneEntity, access));

        public static unsafe HierarchyNodeHandle FromSubScene(SubSceneNodeMapping mapping, World world, Scenes.SubScene subScene)
        {
            var sceneSystem = world.GetExistingSystem<SceneSystem>();
            var statePtr = world.Unmanaged.ResolveSystemState(sceneSystem);
            return statePtr == null ? default : FromSubScene(mapping, world.EntityManager.GetCheckedEntityDataAccess(), SceneSystem.GetSceneEntity(world.Unmanaged, subScene.SceneGUID));
        }

        public static HierarchyNodeHandle FromSubScene(SubSceneNodeMapping subSceneNodeMapping, Scene scene)
        {
            var id = subSceneNodeMapping.GetIdFromScenePath(scene.path);
            return new HierarchyNodeHandle(NodeKind.SubScene, index: id);
        }

        public static HierarchyNodeHandle FromSubScene(SubSceneNodeMapping subSceneNodeMapping, UnloadedScene scene)
        {
            var id = subSceneNodeMapping.GetIdFromScenePath(scene.path);
            return new HierarchyNodeHandle(NodeKind.SubScene, index: id);
        }

        public static HierarchyNodeHandle FromSubScene(SubSceneNodeMapping subSceneNodeMapping, SubScene subScene)
        {
            var id = subSceneNodeMapping.GetIdFromSubSceneComponent(subScene);
            return new HierarchyNodeHandle(NodeKind.SubScene, index: id);
        }

        public override string ToString()
            => Equals(Root) ? $"{nameof(HierarchyNodeHandle)}(Root)" : $"{nameof(HierarchyNodeHandle)}(Kind:{Kind}, Index:{Index}, Version:{Version})";

        public readonly Entity ToEntity()
            => new Entity {Index = Index, Version = Version};

        public GameObject ToGameObject()
        {
            if (Kind != NodeKind.GameObject)
                throw new InvalidOperationException($"Cannot retrieve a GameObject instance from a node of kind {Kind}");

            return EditorUtility.InstanceIDToObject(Index) as GameObject;
        }
    }
}
