using System;
using NUnit.Framework;

namespace Unity.Entities.Tests
{
#if UNITY_DOTSRUNTIME
    public class DotsRuntimeFixmeAttribute : IgnoreAttribute
    {
        public DotsRuntimeFixmeAttribute(string msg = null) : base(msg == null ? "Test should work in DOTS Runtime but currently doesn't. Ignoring until fixed..." : msg)
        {
        }
    }
#else
    public class DotsRuntimeFixmeAttribute : Attribute
    {
        public DotsRuntimeFixmeAttribute(string msg = null)
        {
        }
    }
#endif

#if UNITY_DOTSRUNTIME
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
    internal class IgnoreInPortableTestsAttribute : Attribute
    {
        public IgnoreInPortableTestsAttribute(string reason)
        {
        }
    }

    class ManagedExceptionInPortableTestsAttribute : Attribute
    {
    }
#endif
}
