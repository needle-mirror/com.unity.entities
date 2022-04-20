using System;
using NUnit.Framework;
using Unity.Entities;
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
            using (var myArray = new Collections.NativeArray<int>(10, Collections.Allocator.Persistent))
            {
                TestSystem.TestSimple(myArray);
                Assert.AreEqual(12, myArray[5]);
            }
        }

        [Test]
        public void JobWithDisposeOnCompletion()
        {
            var testArray = new Collections.NativeArray<int>(10, Collections.Allocator.Persistent);

            TestSystem.TestDisposeOnCompletion(testArray);

            Assert.Throws<ObjectDisposedException>(() =>
            {
                testArray[0] = 1;
            });
        }

        public partial class SimpleJobSystem : SystemBase
        {
            protected override void OnUpdate() {}

            static void SetValues(Collections.NativeArray<int> myArray, int value)
            {
                for (int i = 0; i < myArray.Length; i++)
                {
                    myArray[i] = value;
                }
            }

            public void TestSimple(Collections.NativeArray<int> myArray)
            {
                int capturedValue = 12;
                Job.WithCode(() =>
                {
                    SetValues(myArray, capturedValue);
                }).Schedule();
                Dependency.Complete();
            }

            public void TestDisposeOnCompletion(Collections.NativeArray<int> testArray)
            {
                Job.WithDisposeOnCompletion(testArray).WithCode(() =>
                {
                    testArray[0] = 1;
                }).Schedule();
                Dependency.Complete();
            }
        }
    }
}
