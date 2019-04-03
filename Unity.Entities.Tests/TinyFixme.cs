using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
#if UNITY_CSHARP_TINY
    public class TinyFixmeAttribute : IgnoreAttribute
    {
        public TinyFixmeAttribute() : base("Need to fix for Tiny.")
        {
        }
    }
#else
    public class TinyFixmeAttribute : Attribute
    {
    }
#endif
}
