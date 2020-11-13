using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;

namespace Unity.Entities.Tests.ForEachCodegen
{
    [TestFixture]
    public partial class SimpleJobTests : ECSTestsFixture
    {
        private SimpleJobSystem TestSystem;

        [SetUp]
        public void SetUp()
        {
            TestSystem = World.GetOrCreateSystem<SimpleJobSystem>();
        }

        [Test]
        public void SimpleJob()
        {
            using (var myArray = new NativeArray<int>(10, Allocator.Persistent))
            {
                TestSystem.TestMe(myArray);
                Assert.AreEqual(12, myArray[5]);
            }
        }

        public partial class SimpleJobSystem : SystemBase
        {
            protected override void OnUpdate() {}

            static void SetValues(NativeArray<int> myArray, int value)
            {
                for (int i = 0; i < myArray.Length; i++)
                {
                    myArray[i] = value;
                }
            }

            public void TestMe(NativeArray<int> myArray)
            {
                int capturedValue = 12;
                Job.WithCode(() =>
                {
                    SetValues(myArray, capturedValue);
                }).Schedule();
                Dependency.Complete();
            }
        }
    }
}
