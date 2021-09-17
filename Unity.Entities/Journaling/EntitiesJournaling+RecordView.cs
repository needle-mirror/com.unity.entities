#if (UNITY_EDITOR || DEVELOPMENT_BUILD) && !DISABLE_ENTITIES_JOURNALING
using System.Diagnostics;
using System.Linq;

namespace Unity.Entities
{
    partial class EntitiesJournaling
    {
        /// <summary>
        /// Records debugger view.
        /// </summary>
        [DebuggerDisplay("{Index} - {RecordType}, FrameIndex = {FrameIndex}")]
        public readonly struct RecordView
        {
            /// <summary>
            /// The record unique index.
            /// </summary>
            public readonly ulong Index;

            /// <summary>
            /// The record type.
            /// </summary>
            public readonly RecordType RecordType;

            /// <summary>
            /// The record frame index.
            /// </summary>
            public readonly int FrameIndex;

            /// <summary>
            /// The record world view.
            /// </summary>
            public readonly WorldView World;

            /// <summary>
            /// The record executing system view.
            /// </summary>
            public readonly SystemView ExecutingSystem;

            /// <summary>
            /// The record entities view.
            /// </summary>
            public readonly EntityView[] Entities;

            /// <summary>
            /// The record component types view.
            /// </summary>
            public readonly ComponentType[] ComponentTypes;

            /// <summary>
            /// The record data payload.
            /// </summary>
            public readonly object Data;

            /// <summary>
            /// The record origin system view.
            /// In the case of deferred changes, this will tell where the deferred command originated from. Otherwise it will be a default value.
            /// </summary>
            public readonly SystemView OriginSystem;

            internal RecordView(ulong index, RecordType recordType, int frameIndex, ulong worldSequenceNumber, SystemHandleUntyped executingSystem, SystemHandleUntyped originSystem, Entity[] entities, ComponentType[] componentTypes, object data)
            {
                Index = index;
                RecordType = recordType;
                FrameIndex = frameIndex;
                World = new WorldView(worldSequenceNumber);
                ExecutingSystem = new SystemView(executingSystem);
                Entities = entities.Select(entity => new EntityView(entity.Index, entity.Version, worldSequenceNumber)).ToArray();
                ComponentTypes = componentTypes;
                Data = data;
                OriginSystem = new SystemView(originSystem);
            }
        }
    }
}
#endif
