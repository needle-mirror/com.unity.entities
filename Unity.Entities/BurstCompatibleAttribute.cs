using System;

namespace Unity.Entities
{
    /// <summary>
    /// Internal attribute to document and enforce that the tagged method or property has to stay burst compatible.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property)]
    public class BurstCompatibleAttribute : Attribute
    {
    }

    /// <summary>
    /// Internal attribute to state that a method is not burst compatible even though the containing type is.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public class NotBurstCompatibleAttribute : Attribute
    {
    }
}
