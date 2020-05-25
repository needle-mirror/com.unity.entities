using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
#if UNITY_DOTSPLAYER
    public class DotsRuntimeFixmeAttribute : IgnoreAttribute
    {
        public DotsRuntimeFixmeAttribute() : base("Test should work in DOTS Runtime but currently doesn't. Ignoring until fixed...")
        {
        }
    }
#else
    public class DotsRuntimeFixmeAttribute : Attribute
    {
    }
#endif

#if !UNITY_DOTSPLAYER
    public class DotsRuntimeIncompatibleTestAttribute : IgnoreAttribute
    {
        public DotsRuntimeIncompatibleTestAttribute(string reason) : base(reason)
        {
        }
    }
#else
    public class DotsRuntimeIncompatibleTestAttribute : Attribute
    {
        public DotsRuntimeIncompatibleTestAttribute(string reason)
        {
        }
    }
#endif

#if UNITY_PORTABLE_TEST_RUNNER
    internal class IgnoreInPortableTests : IgnoreAttribute
    {
        public IgnoreInPortableTests(string reason) : base(reason)
        {
        }
    }
#else
    internal class IgnoreInPortableTests : Attribute
    {
        public IgnoreInPortableTests(string reason)
        {
        }
    }
#endif
}
