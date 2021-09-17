#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Runtime.InteropServices;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// Information about a record entry.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        readonly struct Record
        {
            public readonly int Position;
            public readonly int Length;
            public readonly ulong Index;
            public readonly RecordType RecordType;
            public readonly int FrameIndex;
            public readonly int EntityCount;
            public readonly int TypeCount;
            public readonly int DataLength;

            public Record(int position, int length, ulong index, RecordType recordType, int frameIndex, int entityCount, int typeCount, int dataLength)
            {
                Position = position;
                Length = length;
                Index = index;
                RecordType = recordType;
                FrameIndex = frameIndex;
                EntityCount = entityCount;
                TypeCount = typeCount;
                DataLength = dataLength;
            }
        }
    }
}
#endif
