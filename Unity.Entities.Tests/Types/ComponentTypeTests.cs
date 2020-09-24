#if UNITY_DOTSRUNTIME
using System;
#endif
using NUnit.Framework;

namespace Unity.Entities.Tests.Types
{
    [TestFixture]
    class ComponentTypeTests : ECSTestsFixture
    {
        struct MockComponentData : IComponentData {}

        [Test]
        public void EqualityOperator_WhenEqual_ReturnsTrue()
        {
            var t1 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 == t2;

            Assert.IsTrue(result);
        }

        [Test]
        public void EqualityOperator_WhenDifferentType_ReturnsFalse()
        {
            var t1 = new ComponentType(typeof(MockComponentData), ComponentType.AccessMode.ReadOnly);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 == t2;

            Assert.IsFalse(result);
        }

        [Test]
        public void EqualityOperator_WhenDifferentAccessMode_ReturnsFalse()
        {
            var t1 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadWrite);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 == t2;

            Assert.IsFalse(result);
        }

        [Test]
        public void InequalityOperator_WhenEqual_ReturnsFalse()
        {
            var t1 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 != t2;

            Assert.IsFalse(result);
        }

        [Test]
        public void InequalityOperator_WhenDifferentType_ReturnsTrue()
        {
            var t1 = new ComponentType(typeof(MockComponentData), ComponentType.AccessMode.ReadOnly);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 != t2;

            Assert.IsTrue(result);
        }

        [Test]
        public void InequalityOperator_WhenDifferentAccessMode_ReturnsTrue()
        {
            var t1 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadWrite);
            var t2 = new ComponentType(typeof(Entity), ComponentType.AccessMode.ReadOnly);

            var result = t1 != t2;

            Assert.IsTrue(result);
        }

        [Test]
        public void DisallowDuplicateTypes()
        {
            #if UNITY_DOTSRUNTIME
                    Assert.Throws<ArgumentException>(() => new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData)));
                    Assert.Throws<ArgumentException>(() => new ComponentTypes(typeof(EcsTestData2), typeof(EcsTestData), typeof(EcsTestData)));
            #else
                    Assert.That(() => new ComponentTypes(typeof(EcsTestData), typeof(EcsTestData2), typeof(EcsTestData)), Throws.ArgumentException
                        .With.Message.Contains($"ComponentTypes cannot contain duplicate types. Remove all but one occurence of \"EcsTestData\""));
                    Assert.That(() => new ComponentTypes(typeof(EcsTestData2), typeof(EcsTestData), typeof(EcsTestData)), Throws.ArgumentException
                        .With.Message.Contains($"ComponentTypes cannot contain duplicate types. Remove all but one occurence of \"EcsTestData\""));
            #endif
        }
    }
}
