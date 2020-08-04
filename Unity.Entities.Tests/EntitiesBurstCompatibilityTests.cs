#if UNITY_EDITOR
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
