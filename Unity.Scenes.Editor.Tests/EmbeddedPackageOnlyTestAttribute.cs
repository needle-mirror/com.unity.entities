using System;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;
using UnityEngine;

namespace Unity.Entities.Tests
{
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method)]
    class EmbeddedPackageOnlyTestAttribute : NUnitAttribute, IApplyToTest
    {
        public void ApplyToTest(Test test)
        {
            var assembly = test.Method?.TypeInfo?.Assembly;
            if (assembly == null)
            {
                Debug.LogError($"The {nameof(EmbeddedPackageOnlyTestAttribute)} attribute can only be applied to tests in an assembly.");
                return;
            }

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(assembly);
            if (package == null)
            {
                Debug.LogError(
                    $"The {nameof(EmbeddedPackageOnlyTestAttribute)} attribute can only be applied to tests in a package.");
                return;
            }

            if (package.source != UnityEditor.PackageManager.PackageSource.Embedded)
            {
                test.RunState = RunState.Ignored;
                test.Properties.Add(PropertyNames.SkipReason, "Only runs in the editor when this package is embedded.");
            }
        }
    }
}
