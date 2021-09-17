using System;
using System.Runtime.InteropServices;
using Unity.Collections;

namespace Unity.Entities
{
    partial class EntitiesProfiler
    {
        [BurstCompatible(RequiredUnityDefine = "ENABLE_PROFILER")]
        [StructLayout(LayoutKind.Sequential)]
        public readonly unsafe struct ArchetypeData : IEquatable<ArchetypeData>
        {
            public readonly ulong StableHash;
            public readonly int ChunkCapacity;
            public readonly int InstanceSize;
            public readonly FixedList512Bytes<ComponentTypeData> ComponentTypes;

            public ArchetypeData(Archetype* archetype)
            {
                StableHash = archetype->StableHash;
                ChunkCapacity = archetype->ChunkCapacity;
                InstanceSize = archetype->InstanceSize;
                ComponentTypes = new FixedList512Bytes<ComponentTypeData>();
                for (var i = 0; i < archetype->TypesCount && i < ComponentTypes.Capacity; ++i)
                {
                    var typeIndex = archetype->Types[i].TypeIndex;
                    var stableTypeIndex = TypeManager.GetTypeInfo(typeIndex).StableTypeHash;
                    var flags = TypeManager.IsChunkComponent(typeIndex) ? ComponentTypeFlags.ChunkComponent : ComponentTypeFlags.None;
                    ComponentTypes.Add(new ComponentTypeData(stableTypeIndex, flags));
                }
            }

            public bool Equals(ArchetypeData other)
            {
                return StableHash == other.StableHash;
            }

            [NotBurstCompatible]
            public override bool Equals(object obj)
            {
                return obj is ArchetypeData archetypeData ? Equals(archetypeData) : false;
            }

            public override int GetHashCode()
            {
                return StableHash.GetHashCode();
            }

            public static bool operator ==(ArchetypeData lhs, ArchetypeData rhs)
            {
                return lhs.Equals(rhs);
            }

            public static bool operator !=(ArchetypeData lhs, ArchetypeData rhs)
            {
                return !lhs.Equals(rhs);
            }
        }
    }
}
