using System;

namespace Unity.Entities.Editor
{
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

        public static HierarchyNodeHandle FromScene(UnityEngine.SceneManagement.Scene scene)
            => new HierarchyNodeHandle(NodeKind.Scene, scene.handle);

        public static HierarchyNodeHandle FromSubScene(Entity entity)
            => new HierarchyNodeHandle(NodeKind.SubScene, entity.Index, entity.Version);
        
        public static unsafe HierarchyNodeHandle FromSubScene(EntityDataAccess* access, SceneTag sceneTag)
            => FromSubScene(access->GetComponentData<Scenes.SceneEntityReference>(sceneTag.SceneEntity).SceneEntity);

        public static HierarchyNodeHandle FromSubScene(World world, Scenes.SubScene subScene)
        {
            var sceneSystem = world.GetExistingSystem<Scenes.SceneSystem>();
            return null == sceneSystem ? default : FromSubScene(sceneSystem.GetSceneEntity(subScene.SceneGUID));
        }

        public override string ToString()
            => Equals(Root) ? $"{nameof(HierarchyNodeHandle)}(Root)" : $"{nameof(HierarchyNodeHandle)}({Kind}:{Index}:{Version})";

        public readonly Entity ToEntity()
            => new Entity {Index = Index, Version = Version};
    }
}