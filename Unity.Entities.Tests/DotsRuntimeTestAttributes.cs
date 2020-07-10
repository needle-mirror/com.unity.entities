using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
#if UNITY_DOTSRUNTIME
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

#if !UNITY_DOTSRUNTIME
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
    class IgnoreInPortableTests : IgnoreAttribute
    {
        public IgnoreInPortableTests(string reason) : base(reason)
        {
        }
    }

    class ManagedExceptionInPortableTests : IgnoreAttribute
    {
        public ManagedExceptionInPortableTests()
            : base("Test uses managed exceptions, which are unsupported in DOTS-Runtime.") { }
    }

#else
    internal class IgnoreInPortableTests : Attribute
    {
        public IgnoreInPortableTests(string reason)
        {
        }
    }

    class ManagedExceptionInPortableTests : Attribute
    {
    }
#endif
}
