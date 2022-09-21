using NUnit.Framework;

namespace Unity.Entities.Hybrid.Tests.Baking
{
    public class TransformUsageFlagsTests
    {
        [Test]
        public void UsageFlagAddWorks()
        {
            var usageFlags = new TransformUsageFlagCounters();
            usageFlags.Add(TransformUsageFlags.Default);
            usageFlags.Add(TransformUsageFlags.ReadGlobalTransform);
            Assert.AreEqual(TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform, usageFlags.Flags);
        }

        [Test]
        public void UsageFlagRemoveWorks()
        {
            var usageFlags = new TransformUsageFlagCounters();
            usageFlags.Add(TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform);
            usageFlags.Add(TransformUsageFlags.Default);
            usageFlags.Remove(TransformUsageFlags.Default);
            Assert.AreEqual(TransformUsageFlags.Default | TransformUsageFlags.ReadGlobalTransform, usageFlags.Flags);
            usageFlags.Remove(TransformUsageFlags.Default);
            Assert.AreEqual(TransformUsageFlags.ReadGlobalTransform, usageFlags.Flags);
        }

        [Test]
        public void UsageFlagUnusedWorks()
        {
            var usageFlags = new TransformUsageFlagCounters();
            Assert.IsTrue(usageFlags.IsUnused);
            usageFlags.Add(TransformUsageFlags.None);
            Assert.IsFalse(usageFlags.IsUnused);
            usageFlags.Remove(TransformUsageFlags.None);
            Assert.IsTrue(usageFlags.IsUnused);
        }
    }
}
