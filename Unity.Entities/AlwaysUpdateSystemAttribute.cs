using System;

namespace Unity.Entities
{
    [AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
    public class AlwaysUpdateSystemAttribute : Attribute
    {
    }
}
