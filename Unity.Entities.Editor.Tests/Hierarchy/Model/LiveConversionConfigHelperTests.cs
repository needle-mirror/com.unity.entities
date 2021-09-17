using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    class LiveConversionConfigHelperTests
    {
        [Test]
        public void EnsureLiveConversionConfigHelperIsProperlyInitialized()
        {
            Assert.That(LiveConversionConfigHelper.IsProperlyInitialized, Is.True);
        }
    }
}
