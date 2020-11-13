using System;
using Unity.Collections;

namespace Unity.Entities
{
    /// <summary>
    /// This component is attached to converted Entities and is guaranteed to be unique within a World. It can be used
    /// to map back to the authoring GameObject from which it was converted. Note that an EntityGuid does not have
    /// enough information to be persistent across sessions.
    /// </summary>
    [Serializable]
    public struct EntityGuid : IComponentData, IEquatable<EntityGuid>, IComparable<EntityGuid>
    {
        // ReSharper disable InconsistentNaming
        /// <summary>This field, when combined with `b`, is for working with EntityGuid as opaque bits (the packing may
        /// change again in the future, as there are still unused bits remaining).</summary>
        public ulong a;
        /// <summary>Use same as `a` field.</summary>
        public ulong b;
        // ReSharper restore InconsistentNaming

        public static readonly EntityGuid Null = new EntityGuid();

        public EntityGuid(int originatingId, byte namespaceId, uint serial)
        {
            a = (ulong)originatingId;
            b = serial | ((ulong)namespaceId << 32);
        }

        internal EntityGuid(int originatingId, uint namespaceId, uint serial)
        {
            a = (ulong)originatingId;
            b = serial | ((ulong)namespaceId << 32);
        }

        /// <summary>Session-unique ID for originating object (typically the authoring GameObject's InstanceID).</summary>
        public int OriginatingId => (int)a;
        /// <summary>An ID that supports multiple primary groupings of converted Entities with the same originating object.
        /// ID zero is reserved for default conversions. Nonzero ID's are for the developer to manage.</summary>
        [Obsolete("This functionality is no longer supported. (RemovedAfter 2021-01-09).")]
        public byte NamespaceId => (byte)(b >> 32);
        internal uint FullNamespaceId => (uint) (b >> 32);
        /// <summary>A unique number used to differentiate Entities associated with the same originating object and namespace.</summary>
        public uint Serial => (uint)b;

        public static bool operator==(in EntityGuid lhs, in EntityGuid rhs) => lhs.a == rhs.a && lhs.b == rhs.b;
        public static bool operator!=(in EntityGuid lhs, in EntityGuid rhs) => !(lhs == rhs);

        public override bool Equals(object obj) => obj is EntityGuid guid && Equals(guid);

        public bool Equals(EntityGuid other) => a == other.a && b == other.b;

        public override int GetHashCode()
        {
            // ReSharper disable NonReadonlyMemberInGetHashCode (readonly fields will not get serialized by unity)
            unchecked { return (a.GetHashCode() * 397) ^ b.GetHashCode(); }
            // ReSharper restore NonReadonlyMemberInGetHashCode
        }

        public int CompareTo(EntityGuid other)
        {
            if (a != other.a)
                return a > other.a ? 1 : -1;

            if (b != other.b)
                return b > other.b ? 1 : -1;

            return 0;
        }

        public override string ToString() => $"{OriginatingId}:{FullNamespaceId:x8}:{Serial:x8}";
    }

    public readonly struct EntityChanges : IDisposable
    {
        readonly EntityChangeSet m_ForwardChangeSet;
        readonly EntityChangeSet m_ReverseChangeSet;

        public EntityChanges(EntityChangeSet forwardChangeSet, EntityChangeSet reverseChangeSet)
        {
            m_ForwardChangeSet = forwardChangeSet;
            m_ReverseChangeSet = reverseChangeSet;
        }

        public bool AnyChanges => HasForwardChangeSet || HasReverseChangeSet;
        public bool HasForwardChangeSet => m_ForwardChangeSet.IsCreated && m_ForwardChangeSet.HasChanges;
        public bool HasReverseChangeSet => m_ReverseChangeSet.IsCreated && m_ReverseChangeSet.HasChanges;

        public EntityChangeSet ForwardChangeSet => m_ForwardChangeSet;
        public EntityChangeSet ReverseChangeSet => m_ReverseChangeSet;

        public void Dispose()
        {
            if (m_ForwardChangeSet.IsCreated)
                m_ForwardChangeSet.Dispose();

            if (m_ReverseChangeSet.IsCreated)
                m_ReverseChangeSet.Dispose();
        }
    }

    /// <summary>
    /// Represents a packed component within an <see cref="EntityChangeSet"/>
    /// </summary>
    public struct PackedComponent
    {
        /// <summary>
        /// Entity index in the packed entities array. <see cref="EntityChangeSet.Entities"/>
        /// </summary>
        public int PackedEntityIndex;

        /// <summary>
        /// Type index in the packed stableTypeHash array. <see cref="EntityChangeSet.TypeHashes"/>
        /// </summary>
        public int PackedTypeIndex;
    }

    /// <summary>
    /// Represents a packed component data change within a <see cref="EntityChangeSet"/>
    /// </summary>
    public struct PackedComponentDataChange
    {
        /// <summary>
        /// The entity and component this change is targeted.
        /// </summary>
        public PackedComponent Component;

        /// <summary>
        /// The start offset for this data change.
        /// </summary>
        /// <remarks>
        /// This is the field offset and NOT the payload offset.
        /// </remarks>
        public int Offset;

        /// <summary>
        /// The size of this data change. This is be the size in <see cref="EntityChangeSet.ComponentData"/> for this entry.
        /// </summary>
        public int Size;
    }

    /// <summary>
    /// Represents an entity reference that was changed within a <see cref="EntityChangeSet"/>
    ///
    /// This structure references the entity by it's unique <see cref="EntityGuid"/>.
    /// </summary>
    /// <remarks>
    /// Multiple patches could exist for the same component with different offsets.
    /// </remarks>
    public struct EntityReferenceChange
    {
        /// <summary>
        /// The entity and component this patched is targeted at.
        /// </summary>
        public PackedComponent Component;

        /// <summary>
        /// The field offset for the <see cref="Entity"/> field.
        /// </summary>
        public int Offset;

        /// <summary>
        /// The entity that the field should reference. Identified by the unique <see cref="EntityGuid"/>.
        /// </summary>
        public EntityGuid Value;
    }

    /// <summary>
    /// Represents a blob asset reference that was changed within a <see cref="EntityChangeSet"/>
    /// </summary>
    public struct BlobAssetReferenceChange
    {
        /// <summary>
        /// The entity and component this patched is targeted at.
        /// </summary>
        public PackedComponent Component;

        /// <summary>
        /// The field offset for the data.
        /// </summary>
        public int Offset;

        /// <summary>
        /// The blob asset this component should point to in the batch.
        /// </summary>
        public ulong Value;
    }

    /// <summary>
    /// Header for a changed blob asset.
    /// </summary>
    public struct BlobAssetChange
    {
        /// <summary>
        /// Byte length of this blob asset in the <see cref="EntityChangeSet.BlobAssetData"/> array.
        /// </summary>
        public int Length;

        /// <summary>
        /// The content hash for this blob asset.
        /// </summary>
        public ulong Hash;
    }

    public struct PackedSharedComponentDataChange
    {
        public PackedComponent Component;
        public object BoxedSharedValue;
    }

    // To consider: Merge PackedManagedComponentDataChange and PackedSharedComponentDataChange
    public struct PackedManagedComponentDataChange
    {
        public PackedComponent Component;
        public object BoxedValue;
    }

    public struct LinkedEntityGroupChange
    {
        public EntityGuid RootEntityGuid;
        public EntityGuid ChildEntityGuid;
    }

    [Flags]
    public enum ComponentTypeFlags
    {
        None = 0,
        ChunkComponent = 1 << 0
    }

    public struct ComponentTypeHash : IEquatable<ComponentTypeHash>
    {
        public ulong StableTypeHash;
        public ComponentTypeFlags Flags;

        public bool Equals(ComponentTypeHash other) => StableTypeHash == other.StableTypeHash && Flags == other.Flags;
        public override bool Equals(object obj) => obj is ComponentTypeHash other && Equals(other);
        public static bool operator==(ComponentTypeHash left, ComponentTypeHash right) => left.Equals(right);
        public static bool operator!=(ComponentTypeHash left, ComponentTypeHash right) => !left.Equals(right);

        public override int GetHashCode()
        {
            unchecked
            {
                return (StableTypeHash.GetHashCode() * 397) ^ (int)Flags;
            }
        }
    }

    /// <summary>
    /// An atomic package of changes to entity and component data.
    /// </summary>
    public readonly struct EntityChangeSet : IDisposable
    {
        /// <summary>
        /// Number of entities from the start of <see cref="Entities"/> that should be considered as created.
        /// </summary>
        public readonly int CreatedEntityCount;

        /// <summary>
        /// Number of entities from the end of <see cref="Entities"/> that should be considered as destroyed.
        /// </summary>
        public readonly int DestroyedEntityCount;

        /// <summary>
        /// A packed array of all entities in this change-set.
        /// </summary>
        public readonly NativeArray<EntityGuid> Entities;

        /// <summary>
        /// A packed array of all types in this change-set.
        /// </summary>
        public readonly NativeArray<ComponentTypeHash> TypeHashes;

        /// <summary>
        /// Names for each entity in this change-set.
        /// </summary>
        public readonly NativeArray<FixedString64> Names;

        /// <summary>
        /// A set of all component additions in this change-set.
        /// </summary>
        public readonly NativeArray<PackedComponent> AddComponents;

        /// <summary>
        /// A set of all component removals in this change-set.
        /// </summary>
        public readonly NativeArray<PackedComponent> RemoveComponents;

        /// <summary>
        /// A set of all component data modifications in this change-set.
        /// </summary>
        public readonly NativeArray<PackedComponentDataChange> SetComponents;

        /// <summary>
        /// Data payload for all component changes specified in <see cref="SetComponents"/>
        /// </summary>
        /// <remarks>
        /// Data changes are tightly packed. Use the <see cref="PackedComponentDataChange.Size"/> to read back.
        /// </remarks>
        public readonly NativeArray<byte> ComponentData;

        /// <summary>
        /// A packed set of all entity references to patch.
        /// </summary>
        public readonly NativeArray<EntityReferenceChange> EntityReferenceChanges;

        /// <summary>
        /// A packed set of all blob asset references to patch.
        /// </summary>
        public readonly NativeArray<BlobAssetReferenceChange> BlobAssetReferenceChanges;

        /// <summary>
        /// A set of all managed component data changes.
        /// </summary>
        public readonly PackedManagedComponentDataChange[] SetManagedComponents;

        /// <summary>
        /// A set of all shared component data changes.
        /// </summary>
        public readonly PackedSharedComponentDataChange[] SetSharedComponents;

        /// <summary>
        /// A set of all linked entity group additions.
        /// </summary>
        public readonly NativeArray<LinkedEntityGroupChange> LinkedEntityGroupAdditions;

        /// <summary>
        /// A set of all linked entity group removals.
        /// </summary>
        public readonly NativeArray<LinkedEntityGroupChange> LinkedEntityGroupRemovals;

        /// <summary>
        /// A set of all blob asset creations in this change set.
        /// </summary>
        /// <remarks>
        /// The <see cref="BlobAssetChange"/> is used to describe the payload within the <see cref="BlobAssetData"/> array.
        /// </remarks>
        public readonly NativeArray<BlobAssetChange> CreatedBlobAssets;

        /// <summary>
        /// A set of all blob assets destroyed in this change set. Identified by the content hash.
        /// </summary>
        public readonly NativeArray<ulong> DestroyedBlobAssets;

        /// <summary>
        /// The payload for all blob assets in this change set.
        /// </summary>
        public readonly NativeArray<byte> BlobAssetData;

        public EntityChangeSet(
            int createdEntityCount,
            int destroyedEntityCount,
            NativeArray<EntityGuid> entities,
            NativeArray<ComponentTypeHash> typeHashes,
            NativeArray<FixedString64> names,
            NativeArray<PackedComponent> addComponents,
            NativeArray<PackedComponent> removeComponents,
            NativeArray<PackedComponentDataChange> setComponents,
            NativeArray<byte> componentData,
            NativeArray<EntityReferenceChange> entityReferenceChanges,
            NativeArray<BlobAssetReferenceChange> blobAssetReferenceChanges,
            PackedManagedComponentDataChange[] setManagedComponents,
            PackedSharedComponentDataChange[] setSharedComponents,
            NativeArray<LinkedEntityGroupChange> linkedEntityGroupAdditions,
            NativeArray<LinkedEntityGroupChange> linkedEntityGroupRemovals,
            NativeArray<BlobAssetChange> createdBlobAssets,
            NativeArray<ulong> destroyedBlobAssets,
            NativeArray<byte> blobAssetData)
        {
            CreatedEntityCount = createdEntityCount;
            DestroyedEntityCount = destroyedEntityCount;
            Entities = entities;
            TypeHashes = typeHashes;
            Names = names;
            AddComponents = addComponents;
            RemoveComponents = removeComponents;
            SetManagedComponents = setManagedComponents;
            SetComponents = setComponents;
            ComponentData = componentData;
            EntityReferenceChanges = entityReferenceChanges;
            BlobAssetReferenceChanges = blobAssetReferenceChanges;
            SetSharedComponents = setSharedComponents;
            LinkedEntityGroupAdditions = linkedEntityGroupAdditions;
            LinkedEntityGroupRemovals = linkedEntityGroupRemovals;
            CreatedBlobAssets = createdBlobAssets;
            DestroyedBlobAssets = destroyedBlobAssets;
            BlobAssetData = blobAssetData;
            IsCreated = true;
        }

        /// <summary>
        /// Returns true if this object is allocated.
        /// </summary>
        public bool IsCreated { get; }

        public bool HasChanges =>
            CreatedEntityCount != 0 ||
            DestroyedEntityCount != 0 ||
            AddComponents.Length != 0 ||
            RemoveComponents.Length != 0 ||
            SetComponents.Length != 0 ||
            ComponentData.Length != 0 ||
            EntityReferenceChanges.Length != 0 ||
            BlobAssetReferenceChanges.Length != 0 ||
            SetManagedComponents.Length != 0 ||
            SetSharedComponents.Length != 0 ||
            LinkedEntityGroupAdditions.Length != 0 ||
            LinkedEntityGroupRemovals.Length != 0 ||
            CreatedBlobAssets.Length != 0 ||
            DestroyedBlobAssets.Length != 0 ||
            BlobAssetData.Length != 0;

        public void Dispose()
        {
            if (!IsCreated)
            {
                return;
            }

            Entities.Dispose();
            TypeHashes.Dispose();
            Names.Dispose();
            AddComponents.Dispose();
            RemoveComponents.Dispose();
            SetComponents.Dispose();
            ComponentData.Dispose();
            EntityReferenceChanges.Dispose();
            BlobAssetReferenceChanges.Dispose();
            LinkedEntityGroupAdditions.Dispose();
            LinkedEntityGroupRemovals.Dispose();
            CreatedBlobAssets.Dispose();
            DestroyedBlobAssets.Dispose();
            BlobAssetData.Dispose();

            foreach (var shared in SetSharedComponents)
                (shared.BoxedSharedValue as IRefCounted)?.Release();

            foreach (var managed in SetManagedComponents)
                (managed.BoxedValue as IDisposable)?.Dispose();
        }

    }

#if !NET_DOTS
    internal static class EntityChangeSetFormatter {
        internal static string PrintSummary(this EntityChangeSet changeSet)
        {
            var sb = new System.Text.StringBuilder();
            PrintSummary(changeSet, sb);
            return sb.ToString();
        }

        internal static void PrintSummary(this EntityChangeSet changeSet, System.Text.StringBuilder sb)
        {
            sb.AppendLine("Change Summary:");
            if (changeSet.CreatedEntityCount > 0)
                sb.AppendLine("\tEntities created: " + changeSet.CreatedEntityCount);
            if (changeSet.DestroyedEntityCount > 0)
                sb.AppendLine("\tEntities destroyed: " + changeSet.DestroyedEntityCount);
            if (changeSet.EntityReferenceChanges.Length > 0)
                sb.AppendLine("\tEntity references changed: " + changeSet.EntityReferenceChanges.Length);
            if (changeSet.AddComponents.Length > 0)
                sb.AppendLine("\tComponents added: " + changeSet.AddComponents.Length);
            if (changeSet.RemoveComponents.Length > 0)
                sb.AppendLine("\tComponents removed: " + changeSet.RemoveComponents.Length);
            if (changeSet.SetComponents.Length > 0)
                sb.AppendLine("\tUnmanaged components changed: " + changeSet.SetComponents.Length);
            if (changeSet.SetManagedComponents.Length > 0)
                sb.AppendLine("\tManaged components changed: " + changeSet.SetManagedComponents.Length);
            if (changeSet.SetSharedComponents.Length > 0)
                sb.AppendLine("\tShared components changed: " + changeSet.SetSharedComponents.Length);
            if (changeSet.CreatedBlobAssets.Length > 0)
                sb.AppendLine("\tBlob assets created: " + changeSet.CreatedBlobAssets.Length);
            if (changeSet.DestroyedBlobAssets.Length > 0)
                sb.AppendLine("\tBlob assets destroyed: " + changeSet.DestroyedBlobAssets.Length);
            if (changeSet.BlobAssetReferenceChanges.Length > 0)
                sb.AppendLine("\tBlob asset references changed: " + changeSet.BlobAssetReferenceChanges.Length);
            if (changeSet.LinkedEntityGroupAdditions.Length > 0)
                sb.AppendLine("\tLinked entity group additions: " + changeSet.LinkedEntityGroupAdditions.Length);
            if (changeSet.LinkedEntityGroupRemovals.Length > 0)
                sb.AppendLine("\tLinked entity group removals: " + changeSet.LinkedEntityGroupRemovals.Length);
            sb.AppendLine();

            if (changeSet.CreatedEntityCount > 0)
            {
                sb.AppendLine("Entities created:");
                for (int i = 0; i < changeSet.CreatedEntityCount; i++)
                {
                    sb.Append('\t');
                    sb.Append(changeSet.Names[i].ToString());
                    sb.Append(" - ");
                    sb.AppendLine(changeSet.Entities[i].ToString());
                }
                sb.AppendLine();
            }

            if (changeSet.DestroyedEntityCount > 0)
            {
                sb.AppendLine("Entities destroyed:");
                int d = changeSet.Names.Length - 1;
                for (int i = 0; i < changeSet.DestroyedEntityCount; i++)
                {
                    sb.Append('\t');
                    sb.Append(changeSet.Names[d - i].ToString());
                    sb.Append(" - ");
                    sb.AppendLine(changeSet.Entities[d - i].ToString());
                }
                sb.AppendLine();
            }

            if (changeSet.AddComponents.Length > 0)
            {
                sb.AppendLine("Components added:");
                for (int i = 0; i < changeSet.AddComponents.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.AddComponents[i], sb);
                sb.AppendLine();
            }

            if (changeSet.RemoveComponents.Length > 0)
            {
                sb.AppendLine("Components removed:");
                for (int i = 0; i < changeSet.RemoveComponents.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.RemoveComponents[i], sb);
                sb.AppendLine();
            }

            if (changeSet.SetComponents.Length > 0)
            {
                sb.AppendLine("Unmanaged components changed:");
                for (int i = 0; i < changeSet.SetComponents.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.SetComponents[i].Component, sb);
                sb.AppendLine();
            }

            if (changeSet.SetManagedComponents.Length > 0)
            {
                sb.AppendLine("Managed components changed:");
                for (int i = 0; i < changeSet.SetManagedComponents.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.SetManagedComponents[i].Component, sb);
                sb.AppendLine();
            }

            if (changeSet.SetSharedComponents.Length > 0)
            {
                sb.AppendLine("Shared components changed:");
                for (int i = 0; i < changeSet.SetSharedComponents.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.SetSharedComponents[i].Component, sb);
                sb.AppendLine();
            }

            if (changeSet.EntityReferenceChanges.Length > 0)
            {
                sb.AppendLine("Entity references changed:");
                for (int i = 0; i < changeSet.EntityReferenceChanges.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.EntityReferenceChanges[i].Component, sb);
                sb.AppendLine();
            }

            if (changeSet.BlobAssetReferenceChanges.Length > 0)
            {
                sb.AppendLine("Blob asset references changed:");
                for (int i = 0; i < changeSet.BlobAssetReferenceChanges.Length; i++)
                    FormatComponentChange(ref changeSet, changeSet.BlobAssetReferenceChanges[i].Component, sb);
                sb.AppendLine();
            }

            if (changeSet.LinkedEntityGroupAdditions.Length > 0)
            {
                sb.AppendLine("Linked entity group additions:");
                for (int i = 0; i < changeSet.LinkedEntityGroupAdditions.Length; i++)
                {
                    sb.Append('\t');
                    sb.Append(changeSet.LinkedEntityGroupAdditions[i].ChildEntityGuid.ToString());
                    sb.Append(" added to ");
                    sb.Append(changeSet.LinkedEntityGroupAdditions[i].RootEntityGuid.ToString());
                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            if (changeSet.LinkedEntityGroupRemovals.Length > 0)
            {
                sb.AppendLine("Linked entity group removals:");
                for (int i = 0; i < changeSet.LinkedEntityGroupRemovals.Length; i++)
                {
                    sb.Append('\t');
                    sb.Append(changeSet.LinkedEntityGroupRemovals[i].ChildEntityGuid.ToString());
                    sb.Append(" removed from ");
                    sb.Append(changeSet.LinkedEntityGroupRemovals[i].RootEntityGuid.ToString());
                    sb.AppendLine();
                }

                sb.AppendLine();
            }
        }

        static void FormatComponentChange(ref EntityChangeSet changeSet, PackedComponent c, System.Text.StringBuilder sb)
        {
            int ti = TypeManager.GetTypeIndexFromStableTypeHash(changeSet.TypeHashes[c.PackedTypeIndex].StableTypeHash);
            var typeName = TypeManager.GetTypeInfo(ti).DebugTypeName;
            sb.Append("\t");
            sb.Append(typeName);
            sb.Append(" - ");
            // Could also print out GUID here
            sb.AppendLine(changeSet.Names[c.PackedEntityIndex].ToString());
        }
    }
#endif
}
