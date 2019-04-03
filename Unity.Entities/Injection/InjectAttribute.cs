using System;

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Field)]
    [Obsolete("Injection API is deprecated. Use ComponentGroup API instead.")]
    public class InjectAttribute : Attribute
    {
    }
}
