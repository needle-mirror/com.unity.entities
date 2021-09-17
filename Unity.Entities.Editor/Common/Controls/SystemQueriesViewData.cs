using System;
using JetBrains.Annotations;

namespace Unity.Entities.Editor
{
    readonly struct SystemQueriesViewData : IEquatable<SystemQueriesViewData>
    {
        public readonly SystemProxy SystemProxy;
        public readonly string SystemName;
        public readonly SystemKind Kind;
        public readonly QueryViewData[] Queries;

        public SystemQueriesViewData(SystemProxy systemProxy, SystemKind kind, [NotNull] QueryViewData[] queries)
        {
            SystemProxy = systemProxy;
            SystemName = systemProxy.NicifiedDisplayName;
            Kind = kind;
            Queries = queries;
        }

        public enum SystemKind : byte
        {
            Regular = 0,
            Unmanaged = 1,
            CommandBufferBegin = 2,
            CommandBufferEnd = 3
        }

        public bool Equals(SystemQueriesViewData other) => Kind == other.Kind && string.Equals(SystemName, other.SystemName, StringComparison.InvariantCultureIgnoreCase);
    }
}
