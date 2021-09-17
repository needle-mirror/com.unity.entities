#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Runtime.InteropServices;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// Data header written in buffer.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        readonly struct Header
        {
            public readonly ulong WorldSequenceNumber;
            public readonly SystemHandleUntyped ExecutingSystem;
            public readonly SystemHandleUntyped OriginSystem;

            public Header(ulong worldSeqNumber, in SystemHandleUntyped executingSystem, in SystemHandleUntyped originSystem)
            {
                WorldSequenceNumber = worldSeqNumber;
                ExecutingSystem = executingSystem;
                OriginSystem = originSystem;
            }
        }
    }
}
#endif
