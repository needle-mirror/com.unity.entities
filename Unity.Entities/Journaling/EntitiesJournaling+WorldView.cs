#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Diagnostics;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// World debugger view.
        /// </summary>
        [DebuggerDisplay("{Name}")]
        public readonly struct WorldView
        {
            /// <summary>
            /// The world sequence number.
            /// </summary>
            public readonly ulong SequenceNumber;

            /// <summary>
            /// The world name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// A reference to the world that matches the sequence number, if it still exists.
            /// </summary>
            public World Reference => GetWorld(SequenceNumber);

            internal WorldView(ulong sequenceNumber)
            {
                SequenceNumber = sequenceNumber;
                Name = s_WorldMap.TryGetValue(sequenceNumber, out var name) ? name : null;
            }
        }
    }
}
#endif
