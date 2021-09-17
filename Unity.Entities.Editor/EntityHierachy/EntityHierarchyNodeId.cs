using System;
using Unity.Collections;

namespace Unity.Entities.Editor
{
    readonly struct EntityHierarchyNodeId : IEquatable<EntityHierarchyNodeId>, IComparable<EntityHierarchyNodeId>
    {
        public readonly NodeKind Kind;
        public readonly int Id;
        public readonly int Version;
        public readonly int HashCode;

        public EntityHierarchyNodeId(NodeKind kind, int id, int version)
        {
            (Kind, Id, Version) = (kind, id, version);
            HashCode = CreateHashCode(kind, id, version);
        }

        public static EntityHierarchyNodeId FromEntity(Entity entity)
            => new EntityHierarchyNodeId(NodeKind.Entity, entity.Index, entity.Version);

        public static EntityHierarchyNodeId FromScene(int sceneId)
            => new EntityHierarchyNodeId(NodeKind.RootScene, sceneId, 0);

        public static EntityHierarchyNodeId FromSubScene(int subsSceneId, bool isDynamicSubScene = false)
            => new EntityHierarchyNodeId(isDynamicSubScene ? NodeKind.DynamicSubScene : NodeKind.SubScene, subsSceneId, 0);

        public static EntityHierarchyNodeId FromName(FixedString64Bytes name)
            => new EntityHierarchyNodeId(NodeKind.Custom, name.GetHashCode(), 0);

        public static readonly EntityHierarchyNodeId Root = new EntityHierarchyNodeId(NodeKind.Root, 0, 0);

        public Entity ToEntity()
        {
            if (Kind != NodeKind.Entity)
                throw new InvalidOperationException($"Cannot convert node of kind {Kind} to {nameof(Entity)}");

            return new Entity {Index = Id, Version = Version};
        }

        public bool TryConvertToEntity(out Entity entity)
        {
            if (Kind != NodeKind.Entity)
            {
                entity = Entity.Null;
                return false;
            }

            entity = new Entity {Index = Id, Version = Version};
            return true;
        }

        public bool Equals(EntityHierarchyNodeId other)
            => Kind == other.Kind && Id == other.Id && Version == other.Version;

        public override bool Equals(object obj)
            => obj is EntityHierarchyNodeId other && Equals(other);

        public int CompareTo(EntityHierarchyNodeId other)
        {
            var kindComparison = ((byte)Kind).CompareTo((byte)other.Kind);
            if (kindComparison != 0) return kindComparison;
            var idComparison = Id.CompareTo(other.Id);
            if (idComparison != 0) return idComparison;
            return Version.CompareTo(other.Version);
        }

        public override int GetHashCode() => HashCode;

        static int CreateHashCode(NodeKind kind, int id, int version)
        {
            unchecked
            {
                var hashCode = (int)kind;
                hashCode = (hashCode * 397) ^ id;
                hashCode = (hashCode * 397) ^ version;
                return hashCode;
            }
        }

        public static bool operator ==(EntityHierarchyNodeId left, EntityHierarchyNodeId right)
            => left.Equals(right);

        public static bool operator !=(EntityHierarchyNodeId left, EntityHierarchyNodeId right)
            => !left.Equals(right);

        public override string ToString() => Equals(Root) ? "Root" : $"{Kind}({Id}:{Version})";
    }

    [Flags]
    enum NodeKind : int
    {
        None             = 0,
        Root             = 1 << 0,
        Entity           = 1 << 1,
        Scene            = 1 << 2,
        RootScene        = 1 << 3 | Scene,
        SubScene         = 1 << 4 | Scene,
        DynamicSubScene  = 1 << 5 | Scene,
        Custom           = 1 << 7
    }
}
