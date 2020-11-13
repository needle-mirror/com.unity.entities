using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace Unity.Entities.Tests
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    class IgnoreBefore20202Attribute : NUnitAttribute, IApplyToTest
    {
        private readonly string _reason;
        public IgnoreBefore20202Attribute(string reason)
        {
            _reason = reason;
        }

        public void ApplyToTest(Test test)
        {
#if !UNITY_2020_2_OR_NEWER
            test.RunState = RunState.Ignored;
            test.Properties.Add(PropertyNames.SkipReason, "Disabled before 2020.2: " + _reason);
#endif
        }
    }
}
