using System;
using Unity.Entities;

namespace Unity.Transforms
{
    /// <summary>
    /// Frozen is added by system when Static is resolved.
    /// Signals that LocalToWorld will no longer be updated.
    /// Read-only from other systems.
    /// User responsible for removing.
    /// </summary>
    [Obsolete("This attribute is no longer in use. (RemovedAfter 2021-01-26)")]
    public struct Frozen : IComponentData
    {
    }
}
