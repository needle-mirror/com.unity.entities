using System.Runtime.InteropServices;
using Unity.Collections;

namespace Unity.Entities
{
    partial class EntitiesProfiler
    {
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct ComponentTypeData
        {
            public readonly ulong StableTypeHash;
            public readonly ComponentTypeFlags Flags;

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
