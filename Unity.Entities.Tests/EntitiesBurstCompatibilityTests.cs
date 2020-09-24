#if UNITY_EDITOR && !UNITY_2020_2_OR_NEWER
// disable on 2020.2 until DOTS-2592 is resolved
using NUnit.Framework;
using Unity.Collections.Tests;

namespace Unity.Entities.Tests
{
    [TestFixture, EmbeddedPackageOnlyTest]
    public class EntitiesBurstCompatibilityTests : BurstCompatibilityTests
    {
        public EntitiesBurstCompatibilityTests()
            : base("Packages/com.unity.entities/Unity.Entities.Tests/_generated_burst_tests.cs",
                  "Unity.Entities")
        {
        }
    }
}
#endif
