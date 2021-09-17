#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System;
using System.Diagnostics;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// System debugger view.
        /// </summary>
        [DebuggerDisplay("{Type}")]
        public readonly struct SystemView
        {
            /// <summary>
            /// The system untyped handle.
            /// </summary>
            public readonly SystemHandleUntyped Handle;

            /// <summary>
            /// The system type.
            /// </summary>
            public readonly Type Type;

            /// <summary>
            /// A reference to the system that matches the system handle, if it still exists.
            /// </summary>
            public ComponentSystemBase Reference => GetSystem(Handle);

            internal SystemView(SystemHandleUntyped system)
            {
                Handle = system;
                Type = s_SystemMap.TryGetValue(system, out var type) ? type : null;
            }
        }
    }
}
#endif
