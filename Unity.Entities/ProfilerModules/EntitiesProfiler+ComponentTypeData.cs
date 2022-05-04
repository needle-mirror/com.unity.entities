using System.Runtime.InteropServices;

namespace Unity.Entities
{
    partial class EntitiesProfiler
    {
        [StructLayout(LayoutKind.Explicit, Size = 9)]
        public readonly struct ComponentTypeData
        {
            [FieldOffset(0)] public readonly ulong StableTypeHash;
            [FieldOffset(8)] public readonly ComponentTypeFlags Flags;

            public ComponentTypeData(ulong stableTypeHash, ComponentTypeFlags flags)
            {
                StableTypeHash = stableTypeHash;
                Flags = flags;
            }

            public int GetTypeIndex()
            {
                var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(StableTypeHash);
                if (typeIndex <= 0)
                    return typeIndex;

                if ((Flags & ComponentTypeFlags.ChunkComponent) != 0)
                    typeIndex = TypeManager.MakeChunkComponentTypeIndex(typeIndex);

                return typeIndex;
            }
        }
    }
}
