#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Diagnostics;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// World debugger view.
        /// </summary>
        [DebuggerDisplay("Entity({Index}:{Version})")]
        public readonly struct EntityView
        {
            /// <summary>
            /// Get entity display name.
            /// </summary>
            /// <param name="index">Entity index.</param>
            /// <param name="version">Entity version</param>
            /// <param name="worldSeqNumber">Entity world sequence number.</param>
            /// <returns>The entity display name.</returns>
            public static string GetDisplayName(int index, int version, ulong worldSeqNumber)
            {
#if !DOTS_DISABLE_DEBUG_NAMES
                var world = GetWorld(worldSeqNumber);
                if (world != null)
                {
                    var name = world.EntityManager.GetName(new Entity { Index = index, Version = version });
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
#endif
                return $"Entity({index}:{version})";
            }

            /// <summary>
            /// The entity index.
            /// </summary>
            public readonly int Index;

            /// <summary>
            /// The entity version.
            /// </summary>
            public readonly int Version;

            /// <summary>
            /// The entity world sequence number.
            /// </summary>
            public readonly ulong WorldSequenceNumber;

            /// <summary>
            /// An entity that matches the index, version and world sequence number, if it exists.
            /// </summary>
            public Entity Entity => GetEntity(Index, Version, WorldSequenceNumber);

            internal EntityView(int index, int version, ulong worldSeqNumber)
            {
                Index = index;
                Version = version;
                WorldSequenceNumber = worldSeqNumber;
            }
        }
    }
}
#endif
